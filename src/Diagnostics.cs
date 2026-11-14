using System;
using System.Globalization;
using System.IO;
using System.Text;

// The ring never retains Exception objects, stack traces, endpoint IDs or paths.
internal sealed class BoundedDiagnostics {
    internal const int Capacity=512, MaxText=160, ExportLimit=1024*1024;
    struct Entry { public long Time, Generation, Count; public string Text; public int HResult; }
    readonly Entry[] entries=new Entry[Capacity];
    int next, count;
    long overwritten;
    internal static string Limit(string s,int max) {
        return String.IsNullOrEmpty(s) ? "" : s.Length<=max ? s : s.Substring(0,max-1)+"…";
    }
    internal static long Increment(long n) { return n==long.MaxValue ? n : n+1; }
    internal void Note(long generation,string text,int hr) {
        text=Limit(text,MaxText);
        lock(entries) {
            int last=(next+Capacity-1)%Capacity;
            if(count!=0 && entries[last].Text==text && entries[last].HResult==hr &&
               entries[last].Generation==generation) {
                entries[last].Count=Increment(entries[last].Count);
                entries[last].Time=DateTime.UtcNow.Ticks;
                return;
            }
            entries[next]=new Entry {Time=DateTime.UtcNow.Ticks,Generation=generation,Count=1,Text=text,HResult=hr};
            next=(next+1)%Capacity;
            if(count==Capacity) overwritten=Increment(overwritten); else count++;
        }
    }
    internal string Counts {
        get { lock(entries) return "诊断事件 "+count+" / "+Capacity+"，已覆盖 "+overwritten; }
    }
    internal void Export(string path,string summary) {
        // One bounded snapshot allows the slow disk/UI to run independently of the owner.
        Entry[] copy;
        long lost;
        lock(entries) {
            copy=new Entry[count];
            for(int i=0;i<count;i++) copy[i]=entries[(next-count+i+Capacity)%Capacity];
            lost=overwritten;
        }
        using(var output=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))
            Write(output,summary,copy,lost);
    }
    static void Write(Stream output,string summary,Entry[] copy,long lost) {
        var utf8=new UTF8Encoding(false);
        var marker=utf8.GetBytes("【已达到导出字节上限，内容已截断】\r\n");
        int written=0;
        for(int i=-1;i<copy.Length;i++) {
            string line;
            if(i<0) line="小米立体声 "+typeof(BoundedDiagnostics).Assembly.GetName().Version+
                "   UTC "+DateTime.UtcNow.ToString("u",CultureInfo.InvariantCulture)+"\r\n"+
                Limit(summary,8192)+"\r\n事件 "+copy.Length+"，已覆盖 "+lost;
            else {
                Entry e=copy[i];
                line=String.Format(CultureInfo.InvariantCulture,"{0:u}  epoch {1}  ×{2}  0x{3:X8}  {4}",
                    new DateTime(e.Time,DateTimeKind.Utc),e.Generation,e.Count,e.HResult,e.Text);
            }
            byte[] bytes=utf8.GetBytes(line+"\r\n");
            if(bytes.Length>ExportLimit-written-marker.Length) {output.Write(marker,0,marker.Length);break;}
            output.Write(bytes,0,bytes.Length); written+=bytes.Length;
        }
    }
}
