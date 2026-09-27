using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using Microsoft.Win32;
namespace Ams2KoreanBeta {
internal static class DepotVerifier {
    internal sealed class Entry { internal string Name,Hash; internal long Size; }
    sealed class Field { internal int Number,Wire; internal byte[] Data; internal ulong Value; }
    static ulong Var(byte[] b,ref int i) {
        ulong n=0; for(int shift=0;shift<64;shift+=7) {
            if(i>=b.Length) throw new InvalidDataException("Steam 목록이 잘렸습니다.");
            byte v=b[i++]; if(shift==63 && v>1) throw new InvalidDataException("Steam 숫자 범위 오류");
            n|=(ulong)(v&127)<<shift; if((v&128)==0)return n;
        } throw new InvalidDataException("Steam 목록 숫자 오류");
    }
    static IEnumerable<Field> Fields(byte[] b) {
        int i=0; while(i<b.Length) {
            ulong tag=Var(b,ref i); var f=new Field{Number=(int)(tag>>3),Wire=(int)(tag&7)};
            if(f.Number==0)throw new InvalidDataException("Steam 필드 오류");
            if(f.Wire==0) f.Value=Var(b,ref i);
            else {
                ulong len=f.Wire==2?Var(b,ref i):f.Wire==1?8UL:f.Wire==5?4UL:UInt64.MaxValue;
                if(len>(ulong)(b.Length-i))throw new InvalidDataException("Steam 필드 길이 오류");
                f.Data=new byte[(int)len]; Buffer.BlockCopy(b,i,f.Data,0,(int)len); i+=(int)len;
            } yield return f;
        }
    }
    internal static Dictionary<string,Entry> Parse(string path) {
        byte[] b=File.ReadAllBytes(path);
        if(b.Length<8||BitConverter.ToUInt32(b,0)!=0x71F617D0||BitConverter.ToUInt32(b,4)>b.Length-8)throw new InvalidDataException("Steam 원본 목록 형식 오류");
        byte[] payload=new byte[(int)BitConverter.ToUInt32(b,4)]; Buffer.BlockCopy(b,8,payload,0,payload.Length);
        var map=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in Fields(payload).Where(f=>f.Number==1&&f.Wire==2)) {
            var e=new Entry(); foreach(var f in Fields(row.Data)) {
                if(f.Number==1&&f.Wire==2)e.Name=Encoding.UTF8.GetString(f.Data).TrimEnd('\0').Replace('\\','/');
                if(f.Number==2&&f.Wire==0)e.Size=checked((long)f.Value);
                if(f.Number==5&&f.Wire==2)e.Hash=BitConverter.ToString(f.Data).Replace("-","");
            }
            if(e.Name!=null && e.Hash!=null && e.Hash.Length==40)map.Add(e.Name,e);
        }
        if(map.Count==0) throw new InvalidDataException("Steam 원본 목록이 비어 있습니다."); return map;
    }
    internal static string Verify(GameInfo game,string[] paths) {
        string before=FileOps.Sha256(game.AppManifest), acf=File.ReadAllText(game.AppManifest);
        var match=Regex.Match(acf,"\"1066891\"\\s*\\{\\s*\"manifest\"\\s*\"([0-9]+)\"");
        if(!match.Success)throw new IOException("Steam 원본 정보가 없습니다. Steam 파일 무결성 검사를 완료한 뒤 다시 확인해주세요.");
        string id=match.Groups[1].Value, manifest=null;
        var roots=new List<string>();
        foreach(var key in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam"})
            foreach(var name in new[]{"SteamPath","InstallPath"}) { var v=Registry.GetValue(key,name,null) as string; if(v!=null)roots.Add(v); }
#if STOCK_RESTORE_TEST
        roots.Clear(); roots.Add(Environment.GetEnvironmentVariable("RECOVERY_TEST_STEAM")??"missing-fixture");
#endif
        foreach(string root in roots) { string p=Path.Combine(root,"depotcache","1066891_"+id+".manifest"); if(File.Exists(p)){manifest=p;break;} }
        if(manifest==null)throw new IOException("Steam 원본 목록을 찾지 못했습니다. Steam 무결성 검사 완료 후 다시 확인해주세요. 복구 완료로 처리하지 않았습니다.");
        var official=Parse(manifest); var errors=new List<string>(); int verified=0;
        foreach(string p in paths) {
            string file=StockRestore.SafeTarget(game.GameDir,p); Entry e;
            if(official.TryGetValue(p,out e)) {
                if(!File.Exists(file)){errors.Add(p+": 원본 없음");continue;}
                string sha; using(var h=SHA1.Create())using(var s=File.OpenRead(file))sha=BitConverter.ToString(h.ComputeHash(s)).Replace("-","");
                if(new FileInfo(file).Length!=e.Size||sha!=e.Hash) errors.Add(p+": 원본과 다름"); else verified++;
            } else if(File.Exists(file)) errors.Add(p+": 패치 추가 파일이 남아 있음");
        }
        if(FileOps.Sha256(game.AppManifest)!=before)throw new IOException("Steam 업데이트 중입니다. 완료 후 다시 확인해주세요.");
        if(errors.Count>0)throw new IOException("원본 복구 미완료 ("+errors.Count+"개). Steam 무결성 검사를 완료해주세요.\n"+String.Join("\n",errors));
        return "한글패치 제거 및 현재 게임 원본 검증 완료\n게임 빌드: "+game.BuildId+" / 원본 확인: "+verified+"개\nSteam 실행 옵션에 -lang=Korean -looseloadtext를 직접 넣었다면 제거해주세요.";
    }
}
}
