using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// One native button/menu/tooltip per panel. Theme selection never restarts audio.
internal sealed class ThemePickerButton : Button {
    readonly Func<UiThemeMode,bool> request;
    readonly ContextMenuStrip menu;
    readonly ToolTip tooltip;
    UiThemeMode mode;
    UiPalette palette;
    internal UiThemeMode Mode {get{return mode;}}
    internal ContextMenuStrip Menu {get{return menu;}}
    internal ThemePickerButton(UiThemeMode initialMode,Func<UiThemeMode,bool> request) {
        UiTheme.ValidateMode(initialMode);this.request=request;
        Name="themePicker";Text="";AccessibleName="界面主题";AccessibleDefaultActionDescription="打开主题菜单";
        Size=new Size(40,36);AutoSize=false;TabStop=true;FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;Cursor=Cursors.Hand;Margin=new Padding(0);
        UiTheme.SetRole(this,UiRole.Icon);
        tooltip=new ToolTip();menu=new ContextMenuStrip {ShowImageMargin=false,ShowCheckMargin=true};
        foreach(UiThemeMode item in new[]{UiThemeMode.System,UiThemeMode.Light,UiThemeMode.Dark}) {
            UiThemeMode choice=item;
            var command=new ToolStripMenuItem(Label(choice)){Name="theme"+choice,CheckOnClick=false};
            command.Click+=(sender,args)=>{
                try {if(this.request==null || this.request(choice))SetSelection(choice);}
                finally {if(!menu.IsDisposed)menu.Close(ToolStripDropDownCloseReason.ItemClicked);}
            };
            menu.Items.Add(command);
        }
        menu.Opening+=(sender,args)=>{ApplyPalette(UiTheme.ForControl(this));UpdateChecks();};
        SetSelection(initialMode);ApplyPalette(UiTheme.Current);
    }
    static string Label(UiThemeMode value) {return value==UiThemeMode.System?"跟随系统":value==UiThemeMode.Light?"浅色":"深色";}
    internal void SetSelection(UiThemeMode value) {
        UiTheme.ValidateMode(value);mode=value;UpdateChecks();
        string description="界面主题："+Label(mode);
        AccessibleDescription=description;tooltip.SetToolTip(this,description);Invalidate();
    }
    void UpdateChecks() {
        for(int i=0;i<menu.Items.Count;i++)((ToolStripMenuItem)menu.Items[i]).Checked=i==(int)mode;
    }
    internal void ApplyPalette(UiPalette value) {
        if(value==null)throw new ArgumentNullException("value");palette=value;
        BackColor=Parent==null?value.Window:Parent.BackColor;ForeColor=value.Text;
        FlatAppearance.BorderSize=value.HighContrast?1:0;FlatAppearance.BorderColor=value.Border;
        FlatAppearance.MouseOverBackColor=value.Hover;FlatAppearance.MouseDownBackColor=value.Pressed;
        tooltip.BackColor=value.Surface;tooltip.ForeColor=value.Text;
        UiTheme.ApplyMenu(menu,value);Invalidate();
    }
    protected override void OnClick(EventArgs e) {
        base.OnClick(e);
        if(IsDisposed || !Enabled || menu.Visible)return;
        menu.Show(this,new Point(Width,Height+(int)Math.Round(3*DeviceDpi/96f)),ToolStripDropDownDirection.BelowLeft);
        if(menu.Visible)menu.Items[(int)mode].Select();
    }
    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e) {
        base.OnPreviewKeyDown(e);
        if(e.KeyCode==Keys.Down || e.KeyCode==Keys.Enter)e.IsInputKey=true;
    }
    protected override void OnKeyDown(KeyEventArgs e) {
        if(e.KeyCode==Keys.Down || e.KeyCode==Keys.Enter) {
            e.Handled=true;e.SuppressKeyPress=true;PerformClick();return;
        }
        base.OnKeyDown(e);
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        var colors=palette ?? UiTheme.ForControl(this);
        float scale=DeviceDpi/96f,edge=Math.Min(24*scale,Math.Min(Width,Height)-10*scale);
        if(edge<8)return;
        var state=e.Graphics.Save();
        try {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            e.Graphics.TranslateTransform((Width-edge)/2,(Height-edge)/2);e.Graphics.ScaleTransform(edge/24,edge/24);
            using(var path=new GraphicsPath())using(var pen=new Pen(Enabled?colors.Text:colors.Muted,1.7f)) {
                pen.LineJoin=LineJoin.Round;
                path.AddBezier(12,3,7,3,3,7,3,12);
                path.AddBezier(3,12,3,17,7,21,12,21);
                path.AddBezier(12,21,17,21,21,17,21,12);
                path.AddBezier(21,12,17.5f,13,14.5f,12,12.5f,9.5f);
                path.AddBezier(12.5f,9.5f,11,7.5f,10.8f,5,12,3);
                path.CloseFigure();e.Graphics.DrawPath(pen,path);
            }
        } finally {e.Graphics.Restore(state);}
        if(Focused && ShowFocusCues) {
            using(var pen=new Pen(colors.HighContrast?colors.Text:colors.Accent,Math.Max(1,scale)))
                e.Graphics.DrawRectangle(pen,2*scale,2*scale,Width-4*scale-1,Height-4*scale-1);
        }
    }
    protected override void Dispose(bool disposing) {
        if(disposing){if(tooltip!=null)tooltip.Dispose();if(menu!=null)menu.Dispose();}
        base.Dispose(disposing);
    }
}
