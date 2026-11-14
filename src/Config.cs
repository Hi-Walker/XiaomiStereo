using System;
using System.Configuration;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;

class ConfigurationException : InvalidOperationException {
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}
class DeviceUnavailableException : InvalidOperationException {
    public DeviceUnavailableException(string message) : base(message) { }
}
class RouteConflictException : InvalidOperationException {
    public RouteConflictException(string message) : base(message) { }
}

// Sole writer: the owner thread in the single-instance application. No timers or histories.
static class StateFile {
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false,true);
    public static byte[] Read(string path,int limit) {
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)) {
            long length=f.Length;
            if(length>limit) throw new ConfigurationException("文件超过大小上限："+Path.GetFileName(path));
            byte[] data=new byte[(int)length];
            int at=0;
            while(at<data.Length) {
                int n=f.Read(data,at,data.Length-at);
                if(n==0) throw new IOException("读取期间文件发生变化。");
                at+=n;
            }
            if(f.ReadByte()!=-1) throw new IOException("读取期间文件发生变化。");
            return data;
        }
    }
    public static Dictionary<string,object> Parse(byte[] bytes,int limit) {
        try {
            var json=new JavaScriptSerializer { MaxJsonLength=limit,RecursionLimit=8 };
            var obj=json.DeserializeObject(Utf8.GetString(bytes).TrimStart('\uFEFF')) as Dictionary<string,object>;
            if(obj==null) throw new FormatException();
            return obj;
        } catch(Exception ex) {
            if(!(ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)) throw;
            throw new ConfigurationException("状态文件不是受支持的 UTF-8 JSON 对象。",ex);
        }
    }
    static bool Equal(byte[] a,byte[] b) {
        if(a.Length!=b.Length) return false;
        for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false;
        return true;
    }
    public static void Write(string path,int limit,Dictionary<string,object> obj) {
        string json=new JavaScriptSerializer { MaxJsonLength=limit, RecursionLimit=8 }.Serialize(obj);
        byte[] bytes=Utf8.GetBytes(json);
        if(bytes.Length>limit) throw new ConfigurationException("状态超过允许大小。");
        if(File.Exists(path) && Equal(Read(path,limit),bytes)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp=path+".tmp";
        using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
            f.Write(bytes,0,bytes.Length);
            f.Flush(true);
        }
        var readback=Read(temp,limit);
        if(!Equal(readback,bytes)) throw new IOException("临时文件校验失败。");
        if(File.Exists(path)) File.Replace(temp,path,null);
        else File.Move(temp,path);
    }
    public static void Keys(Dictionary<string,object> map,params string[] names) {
        foreach(string name in map.Keys) if(Array.IndexOf(names,name)<0) throw new ConfigurationException("状态文件含未知字段："+name.Substring(0,Math.Min(80,name.Length)));
    }
    public static T Get<T>(Dictionary<string,object> map,string name) {
        object value;
        if(!map.TryGetValue(name,out value) || !(value is T)) throw new ConfigurationException("缺少字段或类型错误："+name);
        return (T)value;
    }
    public static string Text(Dictionary<string,object> map,string name,bool optional) {
        object value;
        if(!map.TryGetValue(name,out value)) throw new ConfigurationException("缺少字段："+name);
        if(optional && value==null) return null;
        return CheckText(Get<string>(map,name),optional);
    }
    public static string CheckText(string value,bool optional) {
        if(!String.IsNullOrWhiteSpace(value) && value.Length<=2048 && value.IndexOf('\0')<0) return value;
        if(optional && String.IsNullOrEmpty(value)) return null;
        throw new ConfigurationException("标识符为空、过长或含零字符。");
    }
}

sealed class AppSettings {
    public bool Enabled=true, ManageDefaults=true, SwapChannels;
    public double BalanceDb;
    public string Profile="Stable";
    public string SourceId, LeftId, RightId, FallbackId, SourceContainer, LeftContainer, RightContainer;
    public AppSettings Copy() { return (AppSettings)MemberwiseClone(); }
    internal Dictionary<string,object> Fields() {
        Validate();
        return new Dictionary<string,object> {
            {"schemaVersion",1},{"enabled",Enabled},{"manageDefaults",ManageDefaults},{"swapChannels",SwapChannels},
            {"balanceDb",BalanceDb},{"profile",Profile},{"sourceId",SourceId},{"leftId",LeftId},{"rightId",RightId},
            {"fallbackId",FallbackId},{"sourceContainer",SourceContainer},{"leftContainer",LeftContainer},{"rightContainer",RightContainer}
        };
    }
    public void Validate() {
        if(Double.IsNaN(BalanceDb) || Double.IsInfinity(BalanceDb) || Math.Abs(BalanceDb)>24)
            throw new ConfigurationException("声道平衡范围为 −24 到 +24 dB。");
        if(Profile!="Stable") throw new ConfigurationException("此版本支持 Stable 缓冲档。");
        SourceId=StateFile.CheckText(SourceId,true); LeftId=StateFile.CheckText(LeftId,true);
        RightId=StateFile.CheckText(RightId,true); FallbackId=StateFile.CheckText(FallbackId,true);
        var endpoints=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string id in new[]{SourceId,LeftId,RightId})
            if(id!=null && !endpoints.Add(id)) throw new ConfigurationException("源、左、右必须是不同的播放端点。");
        if(SourceId!=null && DeviceSelector.Same(SourceId,FallbackId))
            throw new ConfigurationException("普通回退输出不能是虚拟源。");
        foreach(string value in new[]{SourceContainer,LeftContainer,RightContainer}) {
            Guid g;
            if(!String.IsNullOrEmpty(value) && !Guid.TryParse(value,out g)) throw new ConfigurationException("无效的设备容器 ID。");
        }
    }
}
sealed class ConfigStore {
    public const int MaxBytes=65536;
    public static string DefaultDirectory {
        get {
            string configured=ConfigurationManager.AppSettings["StateDirectory"];
            if(String.IsNullOrWhiteSpace(configured))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XiaomiStereo");
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        }
    }
    readonly string path;
    public ConfigStore(string directory) { path=Path.Combine(directory,"settings.json"); }
    public AppSettings Load() {
        byte[] bytes;
        try { bytes=StateFile.Read(path,MaxBytes); }
        catch(FileNotFoundException) { return Initial(); }
        catch(DirectoryNotFoundException) { return Initial(); }
        var j=StateFile.Parse(bytes,MaxBytes);
        StateFile.Keys(j,"schemaVersion","enabled","manageDefaults","swapChannels","balanceDb","profile","sourceId","leftId","rightId","fallbackId","sourceContainer","leftContainer","rightContainer");
        if(StateFile.Get<int>(j,"schemaVersion")!=1) throw new ConfigurationException("设置版本不受支持。");
        object db;
        if(!j.TryGetValue("balanceDb",out db) || !(db is int || db is long || db is decimal || db is double))
            throw new ConfigurationException("balanceDb 应为数字。");
        var c=new AppSettings {
            Enabled=StateFile.Get<bool>(j,"enabled"),ManageDefaults=StateFile.Get<bool>(j,"manageDefaults"),
            SwapChannels=StateFile.Get<bool>(j,"swapChannels"),BalanceDb=Convert.ToDouble(db),Profile=StateFile.Get<string>(j,"profile"),
            SourceId=StateFile.Text(j,"sourceId",true),LeftId=StateFile.Text(j,"leftId",true),RightId=StateFile.Text(j,"rightId",true),
            FallbackId=StateFile.Text(j,"fallbackId",true),SourceContainer=StateFile.Text(j,"sourceContainer",true),
            LeftContainer=StateFile.Text(j,"leftContainer",true),RightContainer=StateFile.Text(j,"rightContainer",true)
        };
        c.Validate();
        return c;
    }
    AppSettings Initial() { var initial=new AppSettings(); Save(initial); return initial; }
    public void Save(AppSettings settings) { StateFile.Write(path,MaxBytes,settings.Fields()); }
}
