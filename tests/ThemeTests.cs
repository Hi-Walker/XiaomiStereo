using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Offline UI only: mock view/choices and inert actions. No audio coordinator, tray,
// endpoint enumeration, registry writes, system theme changes, or sound playback.
internal static class ThemeTests {
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int flags);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {internal int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct ComboInfo {
        internal int Size;internal NativeRect Item,Button;internal uint ButtonState;
        internal IntPtr Combo,Edit,List;
    }
    [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr combo,ref ComboInfo info);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
    static int assertions,captionChecks,captionUnsupported;
    static readonly string[] Names={"control","devices","diagnostics","calibration"};
    sealed class Actions {
        internal int Count;
        internal StereoTray.PanelActions Panel() {
            return new StereoTray.PanelActions {
                Enable=value=>Count++, Balance=value=>++Count, Swap=value=>++Count,
                Devices=()=>Count++,Calibration=()=>Count++,Resync=()=>Count++,Diagnostics=()=>Count++
            };
        }
    }
    static void Check(bool condition,string message) {assertions++;if(!condition)throw new Exception(message);}
    static Coordinator.View View() {
        return new Coordinator.View {
            State="正在转发",Enabled=true,ChoiceSerial=1,
            Settings=new AppSettings {SourceId="test-source",LeftId="test-left",RightId="test-right",FallbackId="test-normal"},
            Choices=new[] {
                new EndpointChoice("test-source","小米立体声",null,null),
                new EndpointChoice("test-left","扬声器（智能音箱 Pro-LEFT）",null,null),
                new EndpointChoice("test-right","扬声器（智能音箱 Pro-RIGHT）",null,null),
                new EndpointChoice("test-normal","备用普通扬声器",null,null)
            }
        };
    }
    static Form[] Forms(Actions actions,Icon icon) {
        var view=View();
        return new[] {
            StereoTray.BuildControlPanel(view,icon,actions.Panel()),
            StereoTray.BuildDeviceSettings(view.Settings.Copy(),view.Choices,icon,cfg=>actions.Count++),
            StereoTray.BuildDiagnostics(icon,"状态：正在转发\r\n左侧：Pro-LEFT　右侧：Pro-RIGHT\r\n\r\n此窗口使用离线示例数据，未读取或控制音频设备。",form=>actions.Count++),
            StereoTray.BuildCalibration(icon)
        };
    }
    static Control Find(Form form,string name) {
        var result=form.Controls.Find(name,true);Check(result.Length==1,"missing or duplicate control "+name);return result[0];
    }
    static void ShowOffscreen(Form form) {
        form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);
        form.ShowInTaskbar=false;form.Show();Application.DoEvents();
    }
    static void DisposeForms(Form[] forms) {foreach(var form in forms)form.Dispose();Application.DoEvents();}
    static double Luminance(Color color) {
        double total=0;var values=new[]{color.R,color.G,color.B};var weights=new[]{0.2126,0.7152,0.0722};
        for(int i=0;i<3;i++){double v=values[i]/255.0;total+=weights[i]*(v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4));}return total;
    }
    static double Contrast(Color a,Color b) {double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05);}
    static void Readable(Control control) {
        Check(Contrast(control.ForeColor,control.BackColor)>=4.5,"text contrast below 4.5: "+control.Name);
    }
    static void Caption(Form form,UiPalette palette) {
        int value;int result;
        try {result=DwmGetWindowAttribute(form.Handle,20,out value,4);}
        catch(DllNotFoundException){captionUnsupported++;return;}
        catch(EntryPointNotFoundException){captionUnsupported++;return;}
        if(result<0){captionUnsupported++;return;}
        Check(value==((palette.Dark && !palette.HighContrast)?1:0),"native caption theme not updated: "+form.Text);captionChecks++;
    }
    static void ComboText(ComboBox combo,bool disabled) {
        bool enabled=combo.Enabled;combo.Enabled=!disabled;
        try {
            Check(combo.SelectedIndex>=0 && !String.IsNullOrWhiteSpace(combo.GetItemText(combo.SelectedItem)),"selected device label blank");
            using(var bitmap=new Bitmap(600,32))using(var graphics=Graphics.FromImage(bitmap)) {
                graphics.Clear(combo.BackColor);
                var args=new DrawItemEventArgs(graphics,combo.Font,new Rectangle(0,0,600,32),combo.SelectedIndex,
                    disabled?DrawItemState.Disabled:DrawItemState.None,combo.ForeColor,combo.BackColor);
                typeof(ComboBox).GetMethod("OnDrawItem",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(combo,new object[]{args});
                int foreground=0;
                for(int y=1;y<31;y++)for(int x=2;x<598;x++)if(bitmap.GetPixel(x,y).ToArgb()!=combo.BackColor.ToArgb())foreground++;
                Check(foreground>20,"device owner-draw rendered blank "+(disabled?"disabled":"enabled")+" selection");
            }
        }finally{combo.Enabled=enabled;}
    }
    static void TestPalettes() {
        var actions=new Actions();var forms=Forms(actions,null);
        var edits=forms[0].Tag;var toggle=Find(forms[0],"toggleStereo");var toggleTag=toggle.Tag;
        var balance=(TrackBar)Find(forms[0],"balance");var swap=(CheckBox)Find(forms[0],"swapChannels");
        // Keep non-default edits pending while changing theme; this catches a
        // theme implementation that silently resets controls to default values.
        balance.Value=9;swap.Checked=true;
        int balanceValue=balance.Value;bool swapValue=swap.Checked;
        var source=(ComboBox)Find(forms[1],"sourceDevice");source.SelectedIndex=2;int selected=source.SelectedIndex;
        var manage=(CheckBox)Find(forms[1],"manageDefaults");manage.Checked=false;
        int initial=actions.Count;
        var marker=new object();source.Tag=marker;
        var grid=(DataGridView)Find(forms[3],"calibrationTable");string row=Convert.ToString(grid.Rows[10].Cells[1].Value);
        try {
            foreach(var form in forms)ShowOffscreen(form);
            foreach(var palette in new[]{UiPalette.DarkPalette,UiPalette.LightPalette,UiPalette.ContrastPalette(),UiPalette.DarkPalette}) {
                UiTheme.SetForTesting(palette);
                foreach(var form in forms) {
                    UiTheme.Apply(form,palette);form.Refresh();Application.DoEvents();
                    Check(form.BackColor==palette.Window,"form background: "+form.Text);Caption(form,palette);
                }
                Check(ReferenceEquals(forms[0].Tag,edits) && Object.Equals(toggle.Tag,toggleTag) && ReferenceEquals(source.Tag,marker),"theme replaced application Tags");
                Check(balance.Value==balanceValue && swap.Checked==swapValue && source.SelectedIndex==selected && !manage.Checked,"theme changed input values");
                Check(actions.Count==initial,"theme switch invoked a settings/audio action");
                Check(grid.Rows.Count==21 && Convert.ToString(grid.Rows[10].Cells[1].Value)==row,"theme changed calibration data");
                var diagnostics=(TextBox)Find(forms[2],"diagnosticsText");
                Check(diagnostics.ReadOnly && diagnostics.BackColor==palette.Input && diagnostics.ForeColor==palette.Text,"diagnostics input colors/read-only state");
                Check(source.BackColor==palette.Input && source.ForeColor==palette.Text,"device selector colors");
                Check(!grid.EnableHeadersVisualStyles,"calibration headers still use light visual styles");
                if(!palette.HighContrast) {
                    Readable(Find(forms[0],"leftSpeaker"));Readable(Find(forms[0],"controlDetail"));Readable(toggle);Readable(source);Readable(diagnostics);
                } else {
                    Check(palette.Input==SystemColors.Window && palette.Text==SystemColors.WindowText,"high contrast did not use system text/window colors");
                }
                ComboText(source,false);ComboText(source,true);
            }
            // Recreated HWNDs must retain the caption palette without extra UI actions.
            typeof(Control).GetMethod("RecreateHandle",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(forms[0],null);
            Application.DoEvents();Check(forms[0].IsHandleCreated,"recreated form lost native handle");Caption(forms[0],UiTheme.Current);
            Check(actions.Count==initial && ReferenceEquals(forms[0].Tag,edits),"handle recreation changed settings");
            Check(balance.TabStop && swap.TabStop && source.TabStop,"theme removed keyboard navigation");
        }finally{DisposeForms(forms);}
        Console.WriteLine("PASS four forms: dark/light/high contrast, live theme input/Tag preservation, calibration values, contrast, enabled/disabled device text, keyboard navigation, recreated HWND caption");
    }
    static void TestMenu() {
        using(var menu=new ContextMenuStrip()) {
            var open=menu.Items.Add("打开控制面板");var disabled=menu.Items.Add("当前状态");disabled.Enabled=false;
            menu.Items.Add(new ToolStripSeparator());menu.Items.Add("退出并恢复普通输出");
            foreach(var palette in new[]{UiPalette.DarkPalette,UiPalette.LightPalette,UiPalette.ContrastPalette()}) {
                UiTheme.ApplyMenu(menu,palette);
                Check(menu.BackColor==palette.Surface && menu.ForeColor==palette.Text,"menu palette mismatch");
                Check(open.Enabled && !disabled.Enabled && menu.Items.Count==4,"menu theme changed actions");
                using(var bitmap=new Bitmap(340,120)){menu.Size=bitmap.Size;menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));}
            }
        }
        Console.WriteLine("PASS tray-menu renderer without creating a tray icon");
    }
    static void Key(Control control,Keys key) {
        // Use the WinForms input-key/PreviewKeyDown preprocessing stage before
        // dispatch, as the application message loop does. Direct SendMessage
        // alone would miss radio-button dialog-key navigation interception.
        foreach(int code in new[]{0x0100,0x0101}) { // WM_KEYDOWN / WM_KEYUP
            var message=Message.Create(control.Handle,code,(IntPtr)(int)key,IntPtr.Zero);
            if(control.PreProcessControlMessage(ref message)!=PreProcessControlState.MessageProcessed)
                SendMessage(message.HWnd,message.Msg,message.WParam,message.LParam);
        }
        Application.DoEvents();
    }
    static void ComboChrome(ComboBox combo,UiPalette palette,bool printClient) {
        var info=new ComboInfo{Size=Marshal.SizeOf(typeof(ComboInfo))};
        Check(GetComboBoxInfo(combo.Handle,ref info),"native combo geometry unavailable");
        Rectangle button=Rectangle.FromLTRB(info.Button.Left,info.Button.Top,info.Button.Right,info.Button.Bottom);
        button.Inflate(-2,-2);button.Intersect(combo.ClientRectangle);
        Check(button.Width>=8 && button.Height>=8,"native dropdown button geometry invalid");
        using(var bitmap=new Bitmap(combo.Width,combo.Height)) {
            if(printClient) {
                using(var graphics=Graphics.FromImage(bitmap)) {
                    graphics.Clear(Color.Magenta);IntPtr dc=graphics.GetHdc();
                    try{SendMessage(combo.Handle,0x0318,dc,(IntPtr)12);}finally{graphics.ReleaseHdc(dc);} // WM_PRINTCLIENT, CLIENT|ERASEBKGND
                }
            }else combo.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
            int background=0,arrow=0,bright=0,total=button.Width*button.Height;
            var colors=new Dictionary<int,int>();
            for(int y=button.Top;y<button.Bottom;y++)for(int x=button.Left;x<button.Right;x++) {
                Color pixel=bitmap.GetPixel(x,y);
                int count;colors.TryGetValue(pixel.ToArgb(),out count);colors[pixel.ToArgb()]=count+1;
                if(pixel.ToArgb()==palette.Input.ToArgb() || pixel.ToArgb()==palette.Surface.ToArgb() ||
                    pixel.ToArgb()==palette.Hover.ToArgb() || pixel.ToArgb()==palette.Pressed.ToArgb())background++;
                if(Contrast(pixel,palette.Input)>=3)arrow++;
                if(Luminance(pixel)>0.8)bright++;
            }
            string method=printClient?"WM_PRINTCLIENT":"DrawToBitmap";
            Console.WriteLine("Combo chrome {0}/{1}: item=({2},{3},{4},{5}) button=({6},{7},{8},{9}) sample={10} client={11}; state=0x{12:X}, dropped={13}, enabled={14}, focused={15}; background={16}, arrow={17}, bright={18}, total={19}, input=#{20:X8}",
                palette.Dark?"dark":"light",method,info.Item.Left,info.Item.Top,info.Item.Right,info.Item.Bottom,
                info.Button.Left,info.Button.Top,info.Button.Right,info.Button.Bottom,button,combo.ClientRectangle,
                info.ButtonState,combo.DroppedDown,combo.Enabled,combo.Focused,background,arrow,bright,total,palette.Input.ToArgb());
            if(background<=total/2 || arrow<3 || arrow>=total/2 || (palette.Dark && bright>=total/4)) {
                var common=new List<KeyValuePair<int,int>>(colors);common.Sort((a,b)=>b.Value.CompareTo(a.Value));
                for(int i=0;i<Math.Min(5,common.Count);i++)Console.WriteLine("  Pixel color #{0:X8}: {1}",common[i].Key,common[i].Value);
                string failure=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"theme-combo-failure-"+(palette.Dark?"dark":"light")+"-"+method+".png");
                bitmap.Save(failure,ImageFormat.Png);Console.WriteLine("  Failure image: "+failure);
            }
            Check(background>total/2,"native combo button is not mostly theme background: "+method);
            Check(arrow>=3 && arrow<total/2,"native combo arrow blank or obscures button: "+method);
            if(palette.Dark)Check(bright<total/4,"native combo still has a white dropdown button: "+method);
        }
    }
    static void TestNativeCombo() {
        var view=View();int saved=0,changes=0;
        using(var form=StereoTray.BuildDeviceSettings(view.Settings.Copy(),view.Choices,null,cfg=>saved++)) {
            ShowOffscreen(form);var combo=(ComboBox)Find(form,"sourceDevice");
            Check(combo is ThemeComboBox,"device selector did not use the themed native combobox");
            combo.SelectedIndex=1;combo.SelectedIndexChanged+=(s,e)=>changes++;
            combo.Focus();
            foreach(var palette in new[]{UiPalette.DarkPalette,UiPalette.LightPalette,UiPalette.DarkPalette}) {
                int selected=combo.SelectedIndex,before=changes;
                UiTheme.SetForTesting(palette);UiTheme.Apply(form,palette);combo.Refresh();Application.DoEvents();
                ComboChrome(combo,palette,true);ComboChrome(combo,palette,false);
                Check(combo.SelectedIndex==selected && changes==before && saved==0,"theme/chrome repaint submitted a device selection");
            }
            SendMessage(combo.Handle,0x014F,(IntPtr)1,IntPtr.Zero);Application.DoEvents(); // CB_SHOWDROPDOWN
            Check(SendMessage(combo.Handle,0x0157,IntPtr.Zero,IntPtr.Zero)!=IntPtr.Zero,"native dropdown did not open");
            int previous=changes;Key(combo,Keys.Down);
            Check(combo.SelectedIndex==2 && changes==previous+1,"native dropdown Down key did not select exactly the next item");
            SendMessage(combo.Handle,0x014F,IntPtr.Zero,IntPtr.Zero);Application.DoEvents();
            Check(SendMessage(combo.Handle,0x0157,IntPtr.Zero,IntPtr.Zero)==IntPtr.Zero,"native dropdown did not close");
            previous=changes;Key(combo,Keys.Up);
            Check(combo.SelectedIndex==1 && changes==previous+1,"closed native combo Up key behavior changed");
            Check(saved==0 && combo.TabStop && combo.DropDownStyle==ComboBoxStyle.DropDownList,"native combo navigation submitted settings or lost keyboard/list behavior");
        }
        Console.WriteLine("PASS native combo: themed button/visible arrow via WM_PRINTCLIENT and DrawToBitmap, no selection callbacks on repaint, real dropdown open/close and Up/Down selection");
    }
    static void TestThemePreferences() {
        Check(UiTheme.Resolve(UiThemeMode.System,UiPalette.LightPalette).SameAs(UiPalette.LightPalette),"system mode did not follow light palette");
        Check(UiTheme.Resolve(UiThemeMode.System,UiPalette.DarkPalette).SameAs(UiPalette.DarkPalette),"system mode did not follow dark palette");
        Check(UiTheme.Resolve(UiThemeMode.Light,UiPalette.DarkPalette).SameAs(UiPalette.LightPalette),"manual light did not override system dark");
        Check(UiTheme.Resolve(UiThemeMode.Dark,UiPalette.LightPalette).SameAs(UiPalette.DarkPalette),"manual dark did not override system light");
        var contrast=UiPalette.ContrastPalette();
        foreach(var mode in new[]{UiThemeMode.System,UiThemeMode.Light,UiThemeMode.Dark})
            Check(UiTheme.Resolve(mode,contrast).SameAs(contrast),"high contrast did not override theme preference "+mode);
        string directory=Path.Combine(Path.GetTempPath(),"XiaomiStereo-Theme-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(directory,"ui.json"),settings=Path.Combine(directory,"settings.json");
        var oldTime=new DateTime(2001,2,3,4,5,6,DateTimeKind.Utc);
        const string untouched="audio settings sentinel: do not read or rewrite";
        try {
            var missing=new UiPreferenceStore(directory);
            Check(missing.Mode==UiThemeMode.System && missing.LoadError=="" && !Directory.Exists(directory),"missing preference should be System without creating files");
            Check(!missing.SetMode(UiThemeMode.System) && !Directory.Exists(directory),"unchanged default preference wrote state");
            Directory.CreateDirectory(directory);File.WriteAllText(settings,untouched);File.SetLastWriteTimeUtc(settings,oldTime);
            foreach(var mode in new[]{UiThemeMode.Dark,UiThemeMode.Light,UiThemeMode.System}) {
                Check(missing.SetMode(mode),"changed preference did not save "+mode);
                var loaded=new UiPreferenceStore(directory);
                Check(loaded.Mode==mode && loaded.LoadError=="" && new FileInfo(path).Length<=UiPreferenceStore.MaxBytes,"preference roundtrip failed "+mode);
                File.SetLastWriteTimeUtc(path,oldTime);string content=File.ReadAllText(path);
                Check(!loaded.SetMode(mode) && File.GetLastWriteTimeUtc(path)==oldTime && File.ReadAllText(path)==content,"same preference rewrote ui.json");
            }
            foreach(string invalid in new[]{"","{","{\"schemaVersion\":2,\"theme\":\"dark\"}","{\"schemaVersion\":1,\"theme\":\"invalid\"}","{\"schemaVersion\":1,\"theme\":false}","{\"schemaVersion\":1,\"theme\":\"dark\",\"audio\":1}"}) {
                File.WriteAllText(path,invalid);var bad=new UiPreferenceStore(directory);
                Check(bad.Mode==UiThemeMode.System && !String.IsNullOrEmpty(bad.LoadError),"invalid preference accepted: "+invalid);
                Check(File.ReadAllText(path)==invalid,"invalid preference was silently rewritten during load");
                Check(bad.SetMode(UiThemeMode.System) && bad.LoadError=="" && new UiPreferenceStore(directory).LoadError=="","explicit System did not repair invalid preference");
            }
            File.WriteAllText(path,new string('x',UiPreferenceStore.MaxBytes+1));var large=new UiPreferenceStore(directory);
            Check(large.Mode==UiThemeMode.System && !String.IsNullOrEmpty(large.LoadError),"oversize preference accepted");
            Check(large.SetMode(UiThemeMode.Light) && large.Mode==UiThemeMode.Light && new FileInfo(path).Length<=UiPreferenceStore.MaxBytes,"oversize preference could not be explicitly replaced within bound");
            string accepted=File.ReadAllText(path);bool failed=false;
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)) {
                try{large.SetMode(UiThemeMode.Dark);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
                Check(failed && large.Mode==UiThemeMode.Light,"failed preference write changed accepted mode");
            }
            Check(File.ReadAllText(path)==accepted && new UiPreferenceStore(directory).Mode==UiThemeMode.Light,"failed preference write damaged accepted file");
            Check(File.ReadAllText(settings)==untouched && File.GetLastWriteTimeUtc(settings)==oldTime,"UI preferences touched audio settings");
            Check(Directory.GetFiles(directory).Length<=3,"preference writes accumulated unbounded files");
        }finally {
            if(Directory.Exists(directory)){foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
        }
        Console.WriteLine("PASS theme mode/HC resolution and separate bounded ui.json: roundtrips, malformed/oversize input, no-op writes, failed write preserves accepted mode/file, audio settings untouched");
    }
    static void PickerSelection(Form form,UiThemeMode mode) {
        var picker=(ThemePickerButton)Find(form,"themePicker");
        Check(picker.Mode==mode,"theme button did not retain accepted selection: "+mode);
        foreach(var choice in new[]{UiThemeMode.System,UiThemeMode.Light,UiThemeMode.Dark})
            Check(PickerItem(picker,choice).Checked==(choice==mode),"theme menu is not exactly one accepted selection: "+mode);
    }
    static ToolStripMenuItem PickerItem(ThemePickerButton picker,UiThemeMode mode) {
        var items=picker.Menu.Items.Find("theme"+mode,false);
        Check(items.Length==1 && items[0] is ToolStripMenuItem,"theme menu item missing: "+mode);return (ToolStripMenuItem)items[0];
    }
    static void OpenPicker(ThemePickerButton picker) {
        picker.PerformClick();Application.DoEvents();Check(picker.Menu.Visible,"moon button did not open its menu");
    }
    static void FocusChoice(Form form,string name) {
        var choice=Find(form,name);Check(choice.Focus(),"theme choice could not receive focus: "+name);Application.DoEvents();
        Check(choice.Focused,"theme choice did not retain focus: "+name);
    }
    static void TestThemePicker() {
        var savedMode=UiTheme.Mode;var savedPalette=UiTheme.Current;
        var actions=new Actions();var panel=actions.Panel();var forms=new List<Form>();int requested=0;bool reject=false;
        panel.Theme=mode=>{
            requested++;if(reject)return false;
            UiTheme.SetMode(mode);foreach(var form in forms)UiTheme.Apply(form);return true;
        };
        try {
            UiTheme.SetMode(UiThemeMode.System);
            var main=StereoTray.BuildControlPanel(View(),null,panel,null,UiThemeMode.System);forms.Add(main);
            forms.Add(StereoTray.BuildDiagnostics(null,"Offline theme picker fixture",form=>actions.Count++));
            foreach(var form in forms)ShowOffscreen(form);
            PickerSelection(main,UiThemeMode.System);Check(requested==0 && actions.Count==0,"opening picker submitted an action");
            var picker=(ThemePickerButton)Find(main,"themePicker");
            FocusChoice(main,"themePicker");FocusChoice(main,"diagnostics");FocusChoice(main,"themePicker");
            OpenPicker(picker);picker.Menu.Close();Application.DoEvents();
            PickerSelection(main,UiThemeMode.System);
            Check(requested==0 && actions.Count==0,"focus or opening/closing theme menu submitted a preference or audio action");
            foreach(var key in new[]{Keys.Space,Keys.Enter,Keys.Down}) {
                FocusChoice(main,"themePicker");Key(picker,key);
                Check(picker.Menu.Visible,"moon button keyboard entry did not open menu: "+key);
                Key(picker.Menu,Keys.Escape);Check(!picker.Menu.Visible,"Escape did not close keyboard-opened menu: "+key);
                Check(requested==0 && actions.Count==0,"moon button keyboard entry submitted a preference/audio action: "+key);
            }
            OpenPicker(picker);
            foreach(var palette in new[]{UiPalette.DarkPalette,UiPalette.LightPalette}) {
                UiTheme.Apply(main,palette);Application.DoEvents();
                Check(picker.Menu.Visible && picker.Menu.BackColor==palette.Surface && picker.Menu.ForeColor==palette.Text,"visible theme menu did not update its palette live");
                PickerSelection(main,UiThemeMode.System);
                Check(requested==0 && actions.Count==0,"live menu palette update submitted a preference/audio action");
            }
            picker.Menu.Close();UiTheme.Apply(main);Application.DoEvents();
            var balance=(TrackBar)Find(main,"balance");var swap=(CheckBox)Find(main,"swapChannels");
            balance.Value=7;swap.Checked=true;int initial=actions.Count;object edits=main.Tag;
            foreach(var mode in new[]{UiThemeMode.Light,UiThemeMode.Dark,UiThemeMode.System}) {
                int before=requested;OpenPicker(picker);PickerItem(picker,mode).PerformClick();Application.DoEvents();
                PickerSelection(main,mode);Check(requested==before+1 && UiTheme.Mode==mode,"theme click did not request selected mode exactly once");
                foreach(var form in forms)Check(form.BackColor==UiTheme.Current.Window,"open companion window did not receive selected theme");
                Check(balance.Value==7 && swap.Checked && ReferenceEquals(main.Tag,edits) && actions.Count==initial,"theme choice disturbed pending mix edits or invoked audio actions");
            }
            int keyboardBefore=requested;
            OpenPicker(picker);PickerItem(picker,UiThemeMode.System).Select();Key(picker.Menu,Keys.Down);
            Check(PickerItem(picker,UiThemeMode.Light).Selected,"native theme menu Down key did not navigate");
            Key(picker.Menu,Keys.Escape);Check(!picker.Menu.Visible,"native Escape did not close theme menu");PickerSelection(main,UiThemeMode.System);
            Check(requested==keyboardBefore && actions.Count==initial,"menu navigation or Escape submitted a theme/audio action");
            OpenPicker(picker);PickerItem(picker,UiThemeMode.System).Select();Key(picker.Menu,Keys.Down);Key(picker.Menu,Keys.Enter);
            PickerSelection(main,UiThemeMode.Light);Check(!picker.Menu.Visible && requested==keyboardBefore+1,"native menu Enter did not commit and close exactly once");
            PickerItem(picker,UiThemeMode.System).PerformClick();PickerSelection(main,UiThemeMode.System);
            Check(requested==keyboardBefore+2 && actions.Count==initial,"theme keyboard commit requested extra settings/audio actions");
            reject=true;var accepted=UiTheme.Current;int prior=requested;
            OpenPicker(picker);PickerItem(picker,UiThemeMode.Dark).PerformClick();Application.DoEvents();
            PickerSelection(main,UiThemeMode.System);
            Check(requested==prior+1 && UiTheme.Mode==UiThemeMode.System && UiTheme.Current.SameAs(accepted),"failed theme request changed accepted mode/palette");
            picker.Menu.Close();FocusChoice(main,"diagnostics");FocusChoice(main,"themePicker");OpenPicker(picker);Key(picker.Menu,Keys.Escape);PickerSelection(main,UiThemeMode.System);
            Check(requested==prior+1 && UiTheme.Current.SameAs(accepted),"focus/menu reopen after rejected save retried theme action or changed palette");
            Check(actions.Count==initial && balance.Value==7 && swap.Checked,"rejected theme request changed audio controls");
            StereoTray.RefreshThemePicker(main,UiThemeMode.System);
            Check(requested==prior+1 && actions.Count==initial,"refreshing picker submitted settings");
            using(var passive=StereoTray.BuildControlPanel(View(),null,actions.Panel(),null,UiThemeMode.Light)) {
                ShowOffscreen(passive);PickerItem((ThemePickerButton)Find(passive,"themePicker"),UiThemeMode.Dark).PerformClick();PickerSelection(passive,UiThemeMode.Dark);
                Check(UiTheme.Mode==UiThemeMode.System && UiTheme.Current.SameAs(accepted) && actions.Count==initial,"picker without callback changed global theme/audio");
            }
            using(var closing=StereoTray.BuildControlPanel(View(),null,actions.Panel())) {
                ShowOffscreen(closing);var closingPicker=(ThemePickerButton)Find(closing,"themePicker");OpenPicker(closingPicker);
                var openedMenu=closingPicker.Menu;IntPtr menuWindow=openedMenu.Handle,ownerWindow=closing.Handle;
                closing.Dispose();Application.DoEvents();
                Check(openedMenu.IsDisposed && closingPicker.IsDisposed && !IsWindow(menuWindow) && !IsWindow(ownerWindow),"disposing owner with menu open retained the menu/control/HWND");
                Check(actions.Count==initial,"disposing owner with menu open submitted an audio action");
            }
        }finally{foreach(var form in forms)form.Dispose();UiTheme.SetMode(savedMode);UiTheme.SetForTesting(savedPalette);Application.DoEvents();}
        Console.WriteLine("PASS moon theme menu: accepted checkmark, companion-window/live-popup palette updates, focus/open/close/Escape without writes, Space/Enter/Down button entry, native keyboard commit exactly once, no audio callbacks/pending-edit changes, failed save retained, passive picker, dispose while open");
    }
    static void TestBalanceLayout() {
        foreach(float factor in new[]{1f,1.5f,2f}) {
            var actions=new Actions();using(var form=StereoTray.BuildControlPanel(View(),null,actions.Panel())) {
                ShowOffscreen(form);if(factor!=1f)form.Scale(new SizeF(factor,factor));form.PerformLayout();Application.DoEvents();
                var center=Find(form,"restoreBalance");Rectangle bounds=center.Parent.RectangleToScreen(center.Bounds);
                Check(center.Visible && center.Width>0 && center.Height>0,"restore-center button has no visible size");
                for(Control ancestor=center.Parent;ancestor!=null;ancestor=ancestor.Parent) {
                    Rectangle clip=ancestor.RectangleToScreen(ancestor.ClientRectangle);
                    Check(clip.Contains(bounds),"restore-center border clipped at logical scale "+factor+" by "+ancestor.GetType().Name+": button="+bounds+" ancestor="+clip);
                }
                Check(actions.Count==0,"layout scaling invoked an audio action");
            }
        }
        Console.WriteLine("PASS restore-center full bounds inside every ancestor client at 100/150/200% logical layout scale; no OS DPI change or real-monitor DPI claim");
    }
    static void NativeCycle() {
        int originalForms=Application.OpenForms.Count;var actions=new Actions();var forms=Forms(actions,null);var handles=new IntPtr[forms.Length];
        ContextMenuStrip ownedMenu=null;IntPtr menuHandle=IntPtr.Zero;
        try {
            for(int i=0;i<forms.Length;i++) {
                ShowOffscreen(forms[i]);handles[i]=forms[i].Handle;
                foreach(var palette in new[]{UiPalette.DarkPalette,UiPalette.LightPalette,UiPalette.DarkPalette})UiTheme.Apply(forms[i],palette);
                forms[i].Refresh();
                if(i==0) {
                    var picker=(ThemePickerButton)Find(forms[i],"themePicker");ownedMenu=picker.Menu;
                    OpenPicker(picker);menuHandle=ownedMenu.Handle;ownedMenu.Close();Application.DoEvents();
                }
            }
            Check(actions.Count==0,"lifecycle theme application triggered actions");
        }finally{DisposeForms(forms);}
        Check(Application.OpenForms.Count==originalForms,"disposed themed forms still registered");
        foreach(var handle in handles)Check(!IsWindow(handle),"disposed themed HWND survived");
        Check(ownedMenu!=null && ownedMenu.IsDisposed && !IsWindow(menuHandle),"theme button's owned menu/ HWND survived form disposal");
    }
    static void Settle() {
        var time=Stopwatch.StartNew();while(time.ElapsedMilliseconds<1000){Application.DoEvents();Thread.Sleep(10);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Application.DoEvents();
    }
    static void TestResources() {
        NativeCycle();NativeCycle();Settle();
        using(var process=Process.GetCurrentProcess()) {
            int gdi=GetGuiResources(process.Handle,0),user=GetGuiResources(process.Handle,1);process.Refresh();int handles=process.HandleCount;
            var samples=new int[2,3];
            Console.WriteLine("Theme resource baseline after two warmup cycles: GDI={0}, USER={1}, handles={2}",gdi,user,handles);
            for(int batch=1;batch<=2;batch++) {
                for(int i=0;i<8;i++) {
                    NativeCycle();process.Refresh();
                    Console.WriteLine("Theme resource cycle {0} before fixed batch settling: GDI={1}, USER={2}, handles={3}",(batch-1)*8+i+1,GetGuiResources(process.Handle,0),GetGuiResources(process.Handle,1),process.HandleCount);
                }
                Settle();process.Refresh();
                int afterGdi=GetGuiResources(process.Handle,0),afterUser=GetGuiResources(process.Handle,1),afterHandles=process.HandleCount;
                Console.WriteLine("Theme resources after {0} four-form cycles: GDI {1}->{2}, USER {3}->{4}, handles {5}->{6}",batch*8,gdi,afterGdi,user,afterUser,handles,afterHandles);
                samples[batch-1,0]=afterGdi;samples[batch-1,1]=afterUser;samples[batch-1,2]=afterHandles;
            }
            // Collect both fixed batches before failing so the growth trend is
            // visible. Both samples still use the original baseline/thresholds.
            for(int batch=0;batch<2;batch++)
                Check(samples[batch,0]<=gdi+3 && samples[batch,1]<=user+3 && samples[batch,2]<=handles+4,"themed form lifecycle grew native resources at batch "+(batch+1));
        }
        Console.WriteLine("PASS bounded native lifecycle: 16 four-form cycles plus fixed warmup, no surviving HWNDs or form registrations");
    }
    static void RunResourceChild() {
        // Keep earlier file I/O, focus and DPI-layout test cleanup out of the
        // resource baseline. The child inherits this process's desktop/job;
        // it is this same offline test executable, never the application.
        int outputClosed=0,errorClosed=0;
        using(var child=new Process()) {
            child.StartInfo=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--resources-only") {
                UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
                WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,RedirectStandardOutput=true,RedirectStandardError=true
            };
            child.OutputDataReceived+=(s,e)=>{if(e.Data==null)Interlocked.Exchange(ref outputClosed,1);else Console.WriteLine(e.Data);};
            child.ErrorDataReceived+=(s,e)=>{if(e.Data==null)Interlocked.Exchange(ref errorClosed,1);else Console.Error.WriteLine(e.Data);};
            Check(child.Start(),"could not start isolated offline resource test");
            child.BeginOutputReadLine();child.BeginErrorReadLine();
            if(!child.WaitForExit(90000)) {
                try{child.Kill();}catch(InvalidOperationException){if(!child.HasExited)throw;}
                child.WaitForExit(5000);
                throw new Exception("isolated offline resource test exceeded 90 seconds");
            }
            var drain=Stopwatch.StartNew();
            while((Volatile.Read(ref outputClosed)==0 || Volatile.Read(ref errorClosed)==0) && drain.ElapsedMilliseconds<5000)Thread.Sleep(10);
            Check(Volatile.Read(ref outputClosed)!=0 && Volatile.Read(ref errorClosed)!=0,"isolated resource-test output did not finish within bounded wait");
            Check(child.ExitCode==0,"isolated offline resource test failed: exit "+child.ExitCode);
        }
    }
    static bool Frame(Form form,string path) {
        using(var bitmap=new Bitmap(form.Width,form.Height))using(var graphics=Graphics.FromImage(bitmap)) {
            graphics.Clear(Color.Magenta);IntPtr dc=graphics.GetHdc();bool result;
            try{result=PrintWindow(form.Handle,dc,0);}finally{graphics.ReleaseHdc(dc);}
            if(result)bitmap.Save(path,ImageFormat.Png);return result;
        }
    }
    static void Render(string directory) {
        Check(Path.IsPathRooted(directory),"render output path must be absolute");Directory.CreateDirectory(directory);
        string iconPath=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,@"..\src\XiaomiStereo.ico"));
        using(var icon=File.Exists(iconPath)?new Icon(iconPath):null) {
            foreach(var palette in new[]{UiPalette.DarkPalette,UiPalette.LightPalette}) {
                UiTheme.SetForTesting(palette);var actions=new Actions();var forms=Forms(actions,icon);string mode=palette.Dark?"dark":"light";
                try {
                    for(int i=0;i<forms.Length;i++) {
                        var form=forms[i];ShowOffscreen(form);UiTheme.Apply(form,palette);form.Refresh();Application.DoEvents();
                        using(var bitmap=new Bitmap(form.Width,form.Height)) {
                            form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(directory,mode+"-"+Names[i]+".png"),ImageFormat.Png);
                        }
                        Frame(form,Path.Combine(directory,mode+"-"+Names[i]+"-native-frame.png"));
                        if(i==0) {
                            var picker=(ThemePickerButton)Find(form,"themePicker");OpenPicker(picker);
                            using(var bitmap=new Bitmap(picker.Menu.Width,picker.Menu.Height)) {
                                picker.Menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(directory,mode+"-theme-menu.png"),ImageFormat.Png);
                            }
                            picker.Menu.Close();Application.DoEvents();
                            // Logical layout stress only, not a claim about a real 144-DPI monitor.
                            form.Scale(new SizeF(1.5f,1.5f));form.Refresh();Application.DoEvents();
                            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(directory,mode+"-control-layout150.png"),ImageFormat.Png);}
                        }
                    }
                    Check(actions.Count==0,"preview generation invoked application actions");
                }finally{DisposeForms(forms);}
            }
        }
        File.WriteAllText(Path.Combine(directory,"render-notes.txt"),"Offline mock data only. No audio or Windows setting changes.\r\nDrawToBitmap images verify client rendering. Native-frame images use PrintWindow; inactive/private-desktop DWM composition may be absent.\r\nTitlebar preference is independently checked using DwmGetWindowAttribute when supported.\r\nlayout150 is logical WinForms Scale(1.5), not a real monitor/DPI change or per-monitor-DPI validation.\r\n");
        Console.WriteLine("PASS rendered dark/light four-form previews and logical 150% main-form layouts: "+directory);
    }
    [STAThread] static int Main(string[] args) {
        UiPalette saved=UiTheme.Current;
        try {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            UiTheme.SetForTesting(UiPalette.LightPalette);
            if(args.Length==1 && args[0]=="--resources-only") {
                UiTheme.SetForTesting(UiPalette.DarkPalette);TestResources();
                Console.WriteLine("PASS isolated ThemeTests resources: {0} assertions",assertions);return 0;
            }
            TestThemePreferences();TestPalettes();TestMenu();TestNativeCombo();TestThemePicker();TestBalanceLayout();RunResourceChild();
            if(args.Length==2 && args[0]=="--render")Render(args[1]);
            else Check(args.Length==0,"usage: ThemeTests [--render <absolute-directory>]");
            Console.WriteLine("PASS ThemeTests: {0} assertions, {1} native-caption readbacks; {2} unsupported readbacks skipped",assertions,captionChecks,captionUnsupported);
            return 0;
        }catch(Exception error){Console.WriteLine(error.ToString());return 1;}
        finally{UiTheme.SetForTesting(saved);}
    }
}
