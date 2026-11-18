using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// UI-thread owned and independent of audio settings. No timers, history or startup writes.
internal sealed class UiPreferenceStore {
    internal const int MaxBytes=1024;
    readonly string path;
    UiThemeMode mode=UiThemeMode.System;
    string loadError="";
    internal UiThemeMode Mode {get{return mode;}}
    internal string LoadError {get{return loadError;}}
    internal UiPreferenceStore(string directory) {
        path=Path.Combine(directory,"ui.json");
        try {
            var values=StateFile.Parse(StateFile.Read(path,MaxBytes),MaxBytes);
            StateFile.Keys(values,"schemaVersion","theme");
            if(StateFile.Get<int>(values,"schemaVersion")!=1)
                throw new ConfigurationException("界面偏好版本不受支持。");
            switch(StateFile.Get<string>(values,"theme")) {
                case "system":mode=UiThemeMode.System;break;
                case "light":mode=UiThemeMode.Light;break;
                case "dark":mode=UiThemeMode.Dark;break;
                default:throw new ConfigurationException("界面主题无效。");
            }
        } catch(FileNotFoundException) {} catch(DirectoryNotFoundException) {}
        catch(Exception error) {
            if(error is ConfigurationException || error is ArgumentException || error is NotSupportedException)
                loadError="界面偏好无效，已暂时跟随系统；重新选择主题可修复。";
            else if(error is UnauthorizedAccessException || error is System.Security.SecurityException)
                loadError="无法读取界面偏好，已暂时跟随系统；请检查 ui.json 的访问权限。";
            else if(error is IOException)
                loadError="界面偏好暂时无法读取，已跟随系统；请确认 ui.json 未被其他程序占用。";
            else throw;
        }
    }
    internal bool SetMode(UiThemeMode value) {
        UiTheme.ValidateMode(value);
        if(mode==value && loadError.Length==0)return false;
        var fields=new Dictionary<string,object>{{"schemaVersion",1},{"theme",Token(value)}};
        try {StateFile.Write(path,MaxBytes,fields);}
        catch(ConfigurationException) {
            // StateFile's equality check correctly refuses an oversized old file.
            // Explicit UI selection may repair that file without deleting it first.
            var old=new FileInfo(path);
            if(!old.Exists || old.Length<=MaxBytes)throw;
            ReplaceOversized(value);
        }
        mode=value;loadError="";return true;
    }
    static string Token(UiThemeMode value) {
        return value==UiThemeMode.System?"system":value==UiThemeMode.Light?"light":"dark";
    }
    void ReplaceOversized(UiThemeMode value) {
        // The only variable token is one of three validated ASCII literals.
        byte[] bytes=new UTF8Encoding(false,true).GetBytes("{\"schemaVersion\":1,\"theme\":\""+Token(value)+"\"}");
        if(bytes.Length>MaxBytes)throw new ConfigurationException("界面偏好超过大小上限。");
        string temporary=path+".tmp";
        using(var output=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
            output.Write(bytes,0,bytes.Length);output.Flush(true);
        }
        byte[] readback=StateFile.Read(temporary,MaxBytes);
        if(readback.Length!=bytes.Length)throw new IOException("界面偏好临时文件校验失败。");
        for(int i=0;i<bytes.Length;i++)if(readback[i]!=bytes[i])throw new IOException("界面偏好临时文件校验失败。");
        File.Replace(temporary,path,null);
    }
}
