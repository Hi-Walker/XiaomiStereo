using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal enum UiRole { Default, Surface, Input, Muted, Primary, Success, Error, Icon }
internal enum UiThemeMode { System, Light, Dark }

// Immutable values; controls retain semantic roles separately from their application Tag.
internal sealed class UiPalette {
    internal readonly bool Dark, HighContrast;
    internal readonly Color Window, Surface, Input, Text, Muted, Border, Accent, AccentText;
    internal readonly Color Hover, Pressed, Selection, Success, Error;
    UiPalette(bool dark, bool contrast, Color window, Color surface, Color input, Color text,
        Color muted, Color border, Color accent, Color accentText, Color hover, Color pressed,
        Color selection, Color success, Color error) {
        Dark=dark;HighContrast=contrast;Window=window;Surface=surface;Input=input;Text=text;
        Muted=muted;Border=border;Accent=accent;AccentText=accentText;Hover=hover;Pressed=pressed;
        Selection=selection;Success=success;Error=error;
    }
    static Color R(int r,int g,int b){return Color.FromArgb(r,g,b);}
    internal static readonly UiPalette LightPalette=new UiPalette(false,false,
        R(245,246,248),Color.White,Color.White,R(32,36,43),R(95,103,115),R(210,214,221),
        R(255,105,0),R(30,20,12),R(237,240,244),R(222,227,233),R(255,225,202),R(30,116,76),R(180,45,45));
    internal static readonly UiPalette DarkPalette=new UiPalette(true,false,
        R(30,30,30),R(40,40,40),R(49,49,49),R(244,244,244),R(184,188,195),R(77,77,77),
        R(255,138,61),R(30,20,12),R(62,62,62),R(73,73,73),R(68,82,98),R(135,222,173),R(255,157,157));
    internal static UiPalette ContrastPalette() {
        return new UiPalette(false,true,SystemColors.Window,SystemColors.Window,SystemColors.Window,
            SystemColors.WindowText,SystemColors.WindowText,SystemColors.WindowText,SystemColors.Highlight,
            SystemColors.HighlightText,SystemColors.Control,SystemColors.Highlight,SystemColors.Highlight,
            SystemColors.WindowText,SystemColors.WindowText);
    }
    internal bool SameAs(UiPalette p) {
        return p!=null && Dark==p.Dark && HighContrast==p.HighContrast && Window==p.Window && Surface==p.Surface &&
            Input==p.Input && Text==p.Text && Muted==p.Muted && Border==p.Border && Accent==p.Accent &&
            AccentText==p.AccentText && Hover==p.Hover && Pressed==p.Pressed && Selection==p.Selection &&
            Success==p.Success && Error==p.Error;
    }
}

internal sealed class ThemeForm : Form {
    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        UiTheme.ApplyTitleBar(this,UiTheme.ForControl(this));
    }
}

// Focus is navigation, not a request to save a different preference.
internal sealed class ThemeRadioButton : RadioButton {
    bool entering;
    protected override void OnEnter(EventArgs e) {
        entering=true;try {base.OnEnter(e);}finally {entering=false;}
    }
    protected override void OnClick(EventArgs e) {if(!entering)base.OnClick(e);}
    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e) {
        base.OnPreviewKeyDown(e);
        switch(e.KeyCode) {
            case Keys.Left:case Keys.Right:case Keys.Up:case Keys.Down:case Keys.Home:case Keys.End:
                e.IsInputKey=true;break;
        }
    }
}

// Keep the native drop-down, keyboard navigation and accessibility provider.
// Only its closed control's border and arrow need painting over the light chrome.
internal sealed class ThemeComboBox : ComboBox {
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect {internal int Left,Top,Right,Bottom;internal Rectangle Bounds {get{return Rectangle.FromLTRB(Left,Top,Right,Bottom);}}}
    [StructLayout(LayoutKind.Sequential)]
    struct ComboInfo {internal int Size;internal NativeRect Item,Button;internal int ButtonState;internal IntPtr Combo,Edit,List;}
    [DllImport("user32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GetComboBoxInfo(IntPtr window,ref ComboInfo info);
    [DllImport("gdi32.dll")] static extern int SaveDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool RestoreDC(IntPtr dc,int saved);
    bool pointerInside;
    protected override void WndProc(ref Message m) {
        int message=m.Msg;IntPtr dc=m.WParam;
        bool printing=(message==0x0318 || message==0x0317) && dc!=IntPtr.Zero;
        // Native ComboBox printing can leave the HDC clipped to its text item.
        // Restore the caller's viewport/clip before painting the arrow and border.
        int saved=printing?SaveDC(dc):0;
        try {base.WndProc(ref m);}
        finally {if(saved!=0)RestoreDC(dc,saved);}
        if(IsDisposed || Disposing || !IsHandleCreated || DropDownStyle!=ComboBoxStyle.DropDownList)return;
        var palette=UiTheme.ForControl(this);if(palette.HighContrast)return;
        if(message==0x000F) { // WM_PAINT: the native control has validated its paint region.
            using(var graphics=Graphics.FromHwnd(Handle))PaintChrome(graphics,palette);
        } else if(printing) { // WM_PRINTCLIENT / WM_PRINT
            using(var graphics=Graphics.FromHdc(dc))PaintChrome(graphics,palette);
        }
    }
    void PaintChrome(Graphics graphics,UiPalette palette) {
        var info=new ComboInfo{Size=Marshal.SizeOf(typeof(ComboInfo))};
        if(!GetComboBoxInfo(Handle,ref info) || ClientSize.Width<2 || ClientSize.Height<2)return;
        var client=ClientRectangle;var item=Rectangle.Intersect(client,info.Item.Bounds);
        var button=Rectangle.Intersect(client,info.Button.Bounds);
        if(item.IsEmpty || button.IsEmpty || (info.ButtonState&0x8000)!=0)return;
        // rcItem and rcButton are native pixel coordinates, so no assumed arrow width.
        var saved=graphics.Save();
        try {
            graphics.ExcludeClip(item);
            using(var brush=new SolidBrush(palette.Input))graphics.FillRectangle(brush,client);
        } finally {graphics.Restore(saved);}
        Color buttonColor=Enabled && (DroppedDown || (info.ButtonState&8)!=0)?palette.Pressed:
            Enabled && pointerInside?palette.Hover:palette.Input;
        using(var brush=new SolidBrush(buttonColor))graphics.FillRectangle(brush,button);
        float scale=DeviceDpi/96f;
        float line=Math.Max(1,scale),half=Math.Max(3,3*scale);
        float centerX=button.Left+button.Width/2f,centerY=button.Top+button.Height/2f;
        var smoothing=graphics.SmoothingMode;
        try {
            graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var pen=new Pen(Enabled?palette.Text:palette.Muted,Math.Max(1.5f,1.5f*scale)))
                graphics.DrawLines(pen,new[]{new PointF(centerX-half,centerY-half/2),new PointF(centerX,centerY+half/2),new PointF(centerX+half,centerY-half/2)});
        } finally {graphics.SmoothingMode=smoothing;}
        using(var pen=new Pen(Focused && Enabled?palette.Accent:palette.Border,line))
            graphics.DrawRectangle(pen,line/2,line/2,client.Width-line,client.Height-line);
    }
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);pointerInside=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);pointerInside=false;Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnDropDown(EventArgs e){base.OnDropDown(e);Invalidate();}
    protected override void OnDropDownClosed(EventArgs e){base.OnDropDownClosed(e);Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
}

internal static class UiTheme {
    sealed class ControlTheme {
        internal UiRole Role;
        internal UiPalette Palette;
        internal bool ComboDrawing, FormHandle;
        public ControlTheme() {}
    }
    static readonly ConditionalWeakTable<Control,ControlTheme> states=new ConditionalWeakTable<Control,ControlTheme>();
    static UiThemeMode mode=UiThemeMode.System;
    static UiPalette current=ReadSystem();
    internal static UiPalette Current {get{return current;}}
    internal static UiThemeMode Mode {get{return mode;}}
    internal static void ValidateMode(UiThemeMode value) {
        if(value!=UiThemeMode.System && value!=UiThemeMode.Light && value!=UiThemeMode.Dark)
            throw new ArgumentOutOfRangeException("value");
    }
    internal static UiPalette Resolve(UiThemeMode choice,UiPalette systemPalette) {
        ValidateMode(choice);
        if(systemPalette==null)throw new ArgumentNullException("systemPalette");
        if(systemPalette.HighContrast || choice==UiThemeMode.System)return systemPalette;
        return choice==UiThemeMode.Dark?UiPalette.DarkPalette:UiPalette.LightPalette;
    }
    internal static bool SetMode(UiThemeMode value) {
        ValidateMode(value);mode=value;return RefreshFromSystem();
    }
    internal static UiPalette ReadSystem() {
        if(SystemInformation.HighContrast)return UiPalette.ContrastPalette();
        try {
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                object value=key==null?null:key.GetValue("AppsUseLightTheme");
                if(value is int && (int)value==0)return UiPalette.DarkPalette;
            }
        } catch(System.Security.SecurityException) {} catch(UnauthorizedAccessException) {} catch(System.IO.IOException) {}
        return UiPalette.LightPalette;
    }
    internal static bool RefreshFromSystem() {
        var next=Resolve(mode,ReadSystem());if(current.SameAs(next))return false;current=next;return true;
    }
    internal static void SetForTesting(UiPalette palette) {
        if(palette==null)throw new ArgumentNullException("palette");current=palette;
    }
    internal static UiPalette ForControl(Control control) {
        ControlTheme state;
        for(var c=control;c!=null;c=c.Parent)
            if(states.TryGetValue(c,out state) && state.Palette!=null)return state.Palette;
        return current;
    }
    internal static void SetRole(Control control,UiRole role) {
        if(control==null)throw new ArgumentNullException("control");states.GetOrCreateValue(control).Role=role;
    }
    internal static void Apply(Control root){Apply(root,current);}
    internal static void Apply(Control root,UiPalette palette) {
        if(root==null || root.IsDisposed)return;
        if(palette==null)throw new ArgumentNullException("palette");
        var state=states.GetOrCreateValue(root);state.Palette=palette;
        var form=root as Form;
        Color background=root.Parent==null?palette.Window:root.Parent.BackColor;
        if(form!=null)background=palette.Window;
        if(state.Role==UiRole.Surface)background=palette.Surface;
        if(state.Role==UiRole.Input)background=palette.Input;
        Color foreground=state.Role==UiRole.Muted?palette.Muted:state.Role==UiRole.Success?palette.Success:
            state.Role==UiRole.Error?palette.Error:palette.Text;
        var text=root as TextBoxBase;
        var combo=root as ComboBox;
        var grid=root as DataGridView;
        var button=root as Button;
        var check=root as CheckBox;
        var radio=root as RadioButton;
        bool primary=state.Role==UiRole.Primary;
        if(text!=null || combo!=null || grid!=null)background=palette.Input;
        if(button!=null || (check!=null && check.Appearance==Appearance.Button) || (radio!=null && radio.Appearance==Appearance.Button)) {
            background=state.Role==UiRole.Icon?(root.Parent==null?palette.Window:root.Parent.BackColor):primary?palette.Accent:palette.Surface;
            foreground=primary?palette.AccentText:palette.Text;
        }
        root.BackColor=background;root.ForeColor=foreground;
        if(button!=null) {
            button.UseVisualStyleBackColor=false;
            button.FlatAppearance.BorderColor=palette.Border;
            button.FlatAppearance.MouseOverBackColor=primary?AccentState(palette,false):palette.Hover;
            button.FlatAppearance.MouseDownBackColor=primary?AccentState(palette,true):palette.Pressed;
        }
        if(check!=null) {
            check.UseVisualStyleBackColor=false;
            check.FlatAppearance.BorderColor=palette.Border;
            check.FlatAppearance.CheckedBackColor=palette.Selection;
            check.FlatAppearance.MouseOverBackColor=palette.Hover;
            check.FlatAppearance.MouseDownBackColor=palette.Pressed;
        }
        if(radio!=null) {
            radio.UseVisualStyleBackColor=false;
            radio.FlatAppearance.BorderColor=palette.Border;
            radio.FlatAppearance.CheckedBackColor=primary?palette.Accent:palette.Selection;
            radio.FlatAppearance.MouseOverBackColor=primary?AccentState(palette,false):palette.Hover;
            radio.FlatAppearance.MouseDownBackColor=primary?AccentState(palette,true):palette.Pressed;
        }
        var picker=root as ThemePickerButton;if(picker!=null)picker.ApplyPalette(palette);
        if(combo!=null) {
            combo.FlatStyle=FlatStyle.Flat;
            combo.DrawMode=DrawMode.OwnerDrawFixed;
            combo.ItemHeight=combo.Font.Height+4;
            if(!state.ComboDrawing){combo.DrawItem+=DrawCombo;state.ComboDrawing=true;}
        }
        if(grid!=null) {
            grid.EnableHeadersVisualStyles=false;grid.BackgroundColor=palette.Input;grid.GridColor=palette.Border;
            StyleCells(grid.DefaultCellStyle,palette.Input,palette);
            StyleCells(grid.RowsDefaultCellStyle,palette.Input,palette);
            StyleCells(grid.AlternatingRowsDefaultCellStyle,palette.Surface,palette);
            StyleCells(grid.ColumnHeadersDefaultCellStyle,palette.Surface,palette);
            StyleCells(grid.RowHeadersDefaultCellStyle,palette.Surface,palette);
        }
        if(form!=null) {
            if(!(form is ThemeForm) && !state.FormHandle){form.HandleCreated+=FormHandleCreated;state.FormHandle=true;}
            if(form.IsHandleCreated)ApplyTitleBar(form,palette);
        }
        // Snapshot-free traversal: no controls are created or disposed by applying a palette.
        foreach(Control child in root.Controls)Apply(child,palette);
        root.Invalidate();
    }
    static Color AccentState(UiPalette p,bool pressed) {
        if(p.HighContrast)return SystemColors.Highlight;
        Color other=pressed?Color.Black:Color.White;double amount=pressed?0.12:0.10;
        return Color.FromArgb((int)(p.Accent.R*(1-amount)+other.R*amount),
            (int)(p.Accent.G*(1-amount)+other.G*amount),(int)(p.Accent.B*(1-amount)+other.B*amount));
    }
    static void StyleCells(DataGridViewCellStyle style,Color background,UiPalette p) {
        style.BackColor=background;style.ForeColor=p.Text;
        style.SelectionBackColor=p.Selection;style.SelectionForeColor=p.HighContrast?SystemColors.HighlightText:p.Text;
    }
    static void DrawCombo(object sender,DrawItemEventArgs e) {
        var combo=(ComboBox)sender;var p=ForControl(combo);
        bool selected=(e.State&DrawItemState.Selected)!=0;
        Color background=selected?p.Selection:p.Input;
        Color foreground=!combo.Enabled?SystemColors.GrayText:selected && p.HighContrast?SystemColors.HighlightText:p.Text;
        using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,e.Bounds);
        string text=e.Index>=0 && e.Index<combo.Items.Count?combo.GetItemText(combo.Items[e.Index]):combo.Text;
        var bounds=e.Bounds;bounds.Inflate(-3,0);
        TextRenderer.DrawText(e.Graphics,text,e.Font,bounds,foreground,
            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        if((e.State&DrawItemState.Focus)!=0)ControlPaint.DrawFocusRectangle(e.Graphics,e.Bounds,foreground,background);
    }
    static void FormHandleCreated(object sender,EventArgs e) {
        var form=(Form)sender;ApplyTitleBar(form,ForControl(form));
    }
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    internal static void ApplyTitleBar(Form form,UiPalette p) {
        if(!form.IsHandleCreated)return;
        int dark=p.Dark && !p.HighContrast?1:0;
        try {DwmSetWindowAttribute(form.Handle,20,ref dark,sizeof(int));}
        catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {}
    }
    internal static void ApplyMenu(ContextMenuStrip menu){ApplyMenu(menu,current);}
    internal static void ApplyMenu(ContextMenuStrip menu,UiPalette palette) {
        if(menu==null || menu.IsDisposed)return;
        if(palette==null)throw new ArgumentNullException("palette");
        menu.BackColor=palette.Surface;menu.ForeColor=palette.Text;
        var renderer=menu.Renderer as ThemeMenuRenderer;
        if(renderer==null){renderer=new ThemeMenuRenderer(palette);menu.Renderer=renderer;}
        renderer.Palette=palette;
        foreach(ToolStripItem item in menu.Items){item.BackColor=palette.Surface;item.ForeColor=palette.Text;}
        menu.Invalidate();
    }
    sealed class ThemeMenuRenderer : ToolStripRenderer {
        internal UiPalette Palette;
        internal ThemeMenuRenderer(UiPalette palette){Palette=palette;}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            using(var b=new SolidBrush(Palette.Surface))e.Graphics.FillRectangle(b,e.AffectedBounds);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) {}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            using(var pen=new Pen(Palette.Border))e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            Color color=e.Item.Selected && e.Item.Enabled?Palette.Selection:Palette.Surface;
            using(var b=new SolidBrush(color))e.Graphics.FillRectangle(b,new Rectangle(Point.Empty,e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            e.TextColor=!e.Item.Enabled?SystemColors.GrayText:
                e.Item.Selected && Palette.HighContrast?SystemColors.HighlightText:Palette.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            var box=e.ImageRectangle;if(box.Width<2 || box.Height<2)return;
            Color color=!e.Item.Enabled?SystemColors.GrayText:
                e.Item.Selected && Palette.HighContrast?SystemColors.HighlightText:Palette.Text;
            var state=e.Graphics.Save();
            try {
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var pen=new Pen(color,Math.Max(1.5f,1.7f*e.Graphics.DpiX/96f))) {
                    pen.LineJoin=System.Drawing.Drawing2D.LineJoin.Round;
                    e.Graphics.DrawLines(pen,new[]{new PointF(box.Left+box.Width*0.20f,box.Top+box.Height*0.48f),
                        new PointF(box.Left+box.Width*0.42f,box.Top+box.Height*0.70f),new PointF(box.Left+box.Width*0.82f,box.Top+box.Height*0.25f)});
                }
            } finally {e.Graphics.Restore(state);}
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            using(var pen=new Pen(Palette.Border))e.Graphics.DrawLine(pen,4,e.Item.Height/2,e.Item.Width-5,e.Item.Height/2);
        }
    }
}
