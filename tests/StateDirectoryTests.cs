using System;
using System.IO;
using System.Security;

// A separate AppDomain gives ConfigurationManager its own application config cache.
public sealed class StateDirectoryProbe : MarshalByRefObject {
    public string Read() {return ConfigStore.DefaultDirectory;}
    public string Resolve(string[] args) {return StereoProgram.ResolveStateDirectory(args);}
}
internal static class StateDirectoryTests {
    static void Check(string directory,string value,string expected) {
        string file=Path.Combine(directory,"case.config");
        string entry=value==null ? "" : "<add key=\"StateDirectory\" value=\""+SecurityElement.Escape(value)+"\"/>";
        File.WriteAllText(file,"<configuration><appSettings>"+entry+"</appSettings></configuration>");
        var setup=new AppDomainSetup {ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,ConfigurationFile=file};
        var domain=AppDomain.CreateDomain("StateDirectoryTest",null,setup);
        try {
            var probe=(StateDirectoryProbe)domain.CreateInstanceAndUnwrap(typeof(StateDirectoryProbe).Assembly.FullName,typeof(StateDirectoryProbe).FullName);
            if(probe.Read()!=expected || probe.Read()!=expected)throw new Exception("State directory resolution differs");
            if(probe.Resolve(new string[0])!=expected || probe.Resolve(new[]{"--startup"})!=expected)
                throw new Exception("Manual and startup launches use different state directories");
        } finally {AppDomain.Unload(domain);}
    }
    static void CheckBadDefaultOverride(string directory) {
        string file=Path.Combine(directory,"case.config");
        File.WriteAllText(file,"<configuration><appSettings><add key=\"StateDirectory\" value=\"bad&lt;path\"/></appSettings></configuration>");
        var setup=new AppDomainSetup {ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,ConfigurationFile=file};
        var domain=AppDomain.CreateDomain("StateDirectoryOverrideTest",null,setup);
        try {
            var probe=(StateDirectoryProbe)domain.CreateInstanceAndUnwrap(typeof(StateDirectoryProbe).Assembly.FullName,typeof(StateDirectoryProbe).FullName);
            if(probe.Resolve(new[]{"--startup","--state-dir",directory})!=directory)
                throw new Exception("Explicit state directory did not override invalid default");
            bool rejected=false;
            try {probe.Resolve(new string[0]);}catch(ArgumentException){rejected=true;}
            if(!rejected)throw new Exception("Invalid default path accepted");
            rejected=false;
            try {probe.Resolve(new[]{"--state-dir"});}catch(ArgumentException){rejected=true;}
            if(!rejected)throw new Exception("Missing state directory argument accepted");
        } finally {AppDomain.Unload(domain);}
    }
    static void Main() {
        string directory=Path.Combine(Path.GetTempPath(),"XiaomiStereo-StateDirectory-"+Guid.NewGuid().ToString("N"));
        const string variable="XIAOMI_STEREO_STATE_DIRECTORY_TEST";
        string prior=Environment.GetEnvironmentVariable(variable);
        Directory.CreateDirectory(directory);
        try {
            string fallback=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XiaomiStereo");
            Check(directory,null,fallback);
            Check(directory,"",fallback);
            Check(directory,"  ",fallback);
            Check(directory,@"D:\Program Files (x86)\Xiaomi Stereo\State",@"D:\Program Files (x86)\Xiaomi Stereo\State");
            Environment.SetEnvironmentVariable(variable,directory);
            Check(directory,"%"+variable+"%\\state",Path.GetFullPath(Path.Combine(directory,"state")));
            Check(directory,"relative-state",Path.GetFullPath("relative-state"));
            CheckBadDefaultOverride(directory);
            Console.WriteLine("PASS state directory: defaults, environment expansion, relative normalization, manual/startup agreement, valid explicit path overrides bad default, invalid/missing paths rejected");
        } finally {
            Environment.SetEnvironmentVariable(variable,prior);
            File.Delete(Path.Combine(directory,"case.config"));
            Directory.Delete(directory);
        }
    }
}
