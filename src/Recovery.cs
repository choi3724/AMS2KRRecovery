using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Reflection;
using System.Diagnostics;

namespace Ams2KoreanBeta {
internal static class Recovery {
    internal static string[] Managed() {
        using (var r = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Recovery.Managed"), Encoding.UTF8))
            return r.ReadToEnd().Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries);
    }
    internal static string Base(string root) { return Path.GetDirectoryName(StockRestore.SafeTarget(root,"Backup/AMS2KRRecovery/path-check")); }
    static void State(string dir,string state) {
        string p=Path.Combine(dir,"state.txt");
        string t=p+".tmp"; File.WriteAllText(t,state,new UTF8Encoding(false));
        if(File.Exists(p)) File.Replace(t,p,null); else File.Move(t,p);
    }
    static string Status(string dir) { return File.Exists(Path.Combine(dir,"state.txt")) ? File.ReadAllText(Path.Combine(dir,"state.txt")) : "PREPARING"; }
    internal static List<StockFile> Plan(string root) {
        var files=Managed().Select(p=>new StockFile{Relative=p,Action="remove"}).ToList();
        const string outer="Backup/AMS2-Korean-Upgrade/current/pending.tsv";
        if(File.Exists(StockRestore.SafeTarget(root,outer)))files.Add(new StockFile{Relative=outer,Action="remove"});
        string parent=Path.GetDirectoryName(StockRestore.SafeTarget(root,"Backup/AMS2-Korean/path-check"));
        if(Directory.Exists(parent)) foreach(string dir in Directory.GetDirectories(parent,"AMS2-KR-BETA-*",SearchOption.TopDirectoryOnly)) {
            foreach(string name in new[]{"install-state.tsv","shortcuts.tsv","pending.tsv"}) {
                string p="Backup/AMS2-Korean/"+Path.GetFileName(dir)+"/"+name;
                if(File.Exists(StockRestore.SafeTarget(root,p))) files.Add(new StockFile{Relative=p,Action="remove"});
            }
        }
        StockRestore.AddShortcuts(root,files,delegate(string s){});
        foreach(var f in files) StockRestore.Target(root,f);
        return files;
    }
    static List<StockFile> ReadJournal(string root,string dir) {
        var allowed=new HashSet<string>(Plan(root).Select(f=>StockRestore.Target(root,f)),StringComparer.OrdinalIgnoreCase);
        var result=new List<StockFile>();
        foreach(string line in File.ReadAllLines(Path.Combine(dir,"before.tsv"),Encoding.UTF8).Skip(1)) {
            var r=line.Split('\t');
            if(r.Length!=5) throw new InvalidDataException("복구 기록 형식 오류: "+dir);
            string target=r[4];
            // All targets must still belong to the reviewed list or known metadata.
            bool metadata=r[0].StartsWith("Backup/AMS2-Korean/AMS2-KR-BETA-",StringComparison.OrdinalIgnoreCase)
                && r[0].Split('/').Length==4 && new[]{"install-state.tsv","shortcuts.tsv","pending.tsv"}.Contains(Path.GetFileName(r[0]));
            metadata=metadata||r[0]=="Backup/AMS2-Korean-Upgrade/current/pending.tsv";
            bool shortcut=r[0].StartsWith("shortcut/")&&StockRestore.AllowedShortcut(target)&&StockRestore.IsPatchShortcut(StockRestore.SafeTarget(dir,r[3]),root);
            if(!allowed.Contains(target) && !metadata && !shortcut) throw new InvalidDataException("복구 대상 경로 검증 실패: "+target);
            var f=new StockFile{Relative=r[0],BeforeSha=r[1],BeforeAttributes=(FileAttributes)Int32.Parse(r[2]),BackupName=r[3]};
            if(r[0].StartsWith("shortcut/")) f.ShortcutPath=target;
            if(!String.Equals(StockRestore.Target(root,f),target,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("복구 대상 불일치");
            StockRestore.SafeTarget(dir,f.BackupName);
            result.Add(f);
        }
        return result;
    }
    static void Rollback(string root,string dir,List<StockFile> files) {
        string manifest=SteamLocator.FromGameDirectory(root).AppManifest;
        if(FileOps.Sha256(manifest)!=File.ReadAllText(Path.Combine(dir,"appmanifest.sha256"))) {
            State(dir,"ROLLBACK_FAILED");
            throw new IOException("Steam 게임 정보가 작업 후 변경되었습니다. 이전 파일을 덮어쓰지 않았습니다. Steam 원본 복구를 완료해주세요. 백업: "+dir);
        }
        var errors=new List<string>();
        foreach(var f in files.AsEnumerable().Reverse()) try {
            string p=StockRestore.Target(root,f), current=File.Exists(p)?FileOps.Sha256(p):"ABSENT";
            if(current==f.BeforeSha) {if(current!="ABSENT")File.SetAttributes(p,f.BeforeAttributes);continue;}
            // Never overwrite files downloaded by Steam or subsequently modified by another app.
            if(current!="ABSENT") throw new IOException("작업 이후 변경된 파일은 덮어쓰지 않았습니다");
            if(f.BeforeSha=="ABSENT") continue;
            string source=StockRestore.SafeTarget(dir,f.BackupName);
            if(FileOps.Sha256(source)!=f.BeforeSha) throw new IOException("백업 해시 불일치");
            FileOps.CopyNewExact(source,p,f.BeforeSha); File.SetAttributes(p,f.BeforeAttributes);
        } catch(Exception e) { errors.Add(f.Relative+": "+e.Message); }
        State(dir,errors.Count==0?"ROLLED_BACK":"ROLLBACK_FAILED");
        if(errors.Count>0) throw new IOException("작업 전 복구 실패:\n"+String.Join("\n",errors));
    }
    internal static string Remove(string directory,Action<string> log) {
        var game=SteamLocator.FromGameDirectory(directory); string root=game.GameDir;
        using(var mutex=new Mutex(false,"Local\\AMS2KRRecovery-"+FileOps.Sha256Text(root.ToUpperInvariant()))) {
            bool held=false;
            try {
                try { held=mutex.WaitOne(0); } catch(AbandonedMutexException){held=true;}
                if(!held) throw new IOException("같은 게임의 삭제 작업이 이미 실행 중입니다.");
                StockRestore.GuardProcesses(); StockRestore.GuardContentManager(root);
                string parent=Base(root); Directory.CreateDirectory(parent);
                foreach(string old in Directory.GetDirectories(parent).OrderBy(x=>x)) {
                    StockRestore.SafeTarget(root,"Backup/AMS2KRRecovery/"+Path.GetFileName(old)+"/state.txt");
                    string state=Status(old);
                    if(state=="REMOVING"||state=="ROLLBACK_FAILED") Rollback(root,old,ReadJournal(root,old));
                }
                string dir=Path.Combine(parent,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
                Directory.CreateDirectory(dir);
                var report=log;
                log=s=>{File.AppendAllText(Path.Combine(dir,"events.log"),DateTime.UtcNow.ToString("o")+" "+s+Environment.NewLine,new UTF8Encoding(false));if(report!=null)report(s);};
                var files=Plan(root); State(dir,"PREPARING");
                string manifestHash=FileOps.Sha256(game.AppManifest);
                File.WriteAllText(Path.Combine(dir,"appmanifest.sha256"),manifestHash);
                string cmPath=StockRestore.SafeTarget(root,"Mods/state.json");
                string cmHash=File.Exists(cmPath)?FileOps.Sha256(cmPath):"ABSENT";
                try {
                    log("삭제 전 파일을 백업하고 있습니다..."); int index=0;
                    foreach(var f in files) {
                        string p=StockRestore.Target(root,f); f.BackupName="before/"+(index++).ToString("D4");
                        f.BeforeSha=File.Exists(p)?FileOps.Sha256(p):"ABSENT";
                        if(f.BeforeSha=="ABSENT") continue;
                        using(new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.None)){}
                        f.BeforeAttributes=File.GetAttributes(p);
                        FileOps.CopyNewExact(p,StockRestore.SafeTarget(dir,f.BackupName),f.BeforeSha);
                    }
                    File.WriteAllText(Path.Combine(dir,"before.tsv"),"relative_path\tsha256\tattributes\tbackup\ttarget\n"+String.Join("\n",files.Select(f=>f.Relative+"\t"+f.BeforeSha+"\t"+(int)f.BeforeAttributes+"\t"+f.BackupName+"\t"+StockRestore.Target(root,f)))+"\n",new UTF8Encoding(false));
                    StockRestore.GuardProcesses(); StockRestore.GuardContentManager(root);
                    State(dir,"REMOVING"); int count=0;
                    foreach(var f in files) {
                        if(FileOps.Sha256(game.AppManifest)!=manifestHash) throw new IOException("Steam 업데이트가 감지되어 작업을 중단했습니다.");
                        if((File.Exists(cmPath)?FileOps.Sha256(cmPath):"ABSENT")!=cmHash)throw new IOException("CM 작업이 감지되어 중단했습니다.");
                        string p=StockRestore.Target(root,f), current=File.Exists(p)?FileOps.Sha256(p):"ABSENT";
                        if(current!=f.BeforeSha) throw new IOException("백업 이후 파일이 변경되었습니다: "+f.Relative);
                        if(current=="ABSENT") continue;
                        File.SetAttributes(p,File.GetAttributes(p)&~FileAttributes.ReadOnly); File.Delete(p);
                        if(++count%50==0) log("패치 대상 정리: "+count+"개");
#if STOCK_RESTORE_TEST
                        if(Environment.GetEnvironmentVariable("RECOVERY_FAIL_AFTER")==count.ToString()) throw new IOException("TEST injected failure");
                        if(Environment.GetEnvironmentVariable("RECOVERY_CRASH_AFTER")==count.ToString()) Environment.Exit(77);
#endif
                    }
                    foreach(var f in files)if(File.Exists(StockRestore.Target(root,f)))throw new IOException("삭제 후 파일이 다시 생성되었습니다: "+f.Relative);
                    State(dir,"WAITING_FOR_STEAM"); ShortcutManager.RefreshShell();
                    log("패치 대상 정리 완료. Steam 원본 복구가 필요합니다."); return dir;
                } catch(Exception e) {
                    File.AppendAllText(Path.Combine(dir,"events.log"),"FAILED: "+e+Environment.NewLine);
                    if(Status(dir)=="REMOVING") {
                        try { Rollback(root,dir,files); }
                        catch(Exception rollback){throw new IOException(e.Message+"\n"+rollback.Message+"\n백업: "+dir,e);}
                    }
                    throw new IOException(e.Message+"\n상태: "+Status(dir)+"\n백업: "+dir,e);
                }
            } finally {if(held)mutex.ReleaseMutex();}
        }
    }
    internal static void RequestSteam() {
        Process.Start(new ProcessStartInfo("steam://validate/1066890"){UseShellExecute=true});
    }
    internal static string Verify(string directory) {
        var game=SteamLocator.FromGameDirectory(directory);
        string result=DepotVerifier.Verify(game,Managed());
        var remaining=Plan(game.GameDir).Where(f=>!Managed().Contains(f.Relative)&&File.Exists(StockRestore.Target(game.GameDir,f))).Select(f=>f.Relative).ToArray();
        if(remaining.Length>0)throw new IOException("게임 원본은 확인했지만 패치 실행 기록/바로가기가 남아 있습니다. [완전 삭제 및 복구 시작]으로 정리해주세요.\n"+String.Join("\n",remaining));
        string parent=Base(game.GameDir);
        if(Directory.Exists(parent)) foreach(string d in Directory.GetDirectories(parent))
            if(Status(d)=="WAITING_FOR_STEAM") State(d,"VERIFIED");
        return result;
    }
}
}
