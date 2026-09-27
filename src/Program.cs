using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
[assembly:AssemblyVersion("0.1.0.0")]
[assembly:AssemblyFileVersion("0.1.0.0")]
[assembly:AssemblyProduct("AMS2 한국어 패치 완전 삭제")]
namespace Ams2KoreanBeta {
internal sealed class RecoveryForm:Form {
    TextBox path=new TextBox(),log=new TextBox(); Button remove=new Button(),verify=new Button(),steam=new Button(); bool busy;
    internal RecoveryForm() {
        Text="AMS2 한국어 패치 완전 삭제 0.1"; ClientSize=new Size(760,510); Font=new Font("Malgun Gothic",10);
        StartPosition=FormStartPosition.CenterScreen; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false;
        Controls.Add(new Label{Text="AMS2 한국어 패치 완전 삭제 0.1",Left=22,Top=20,Width=710,Height=35,Font=new Font(Font.FontFamily,19,FontStyle.Bold)});
        Controls.Add(new Label{Text="모든 배포 버전 대상 · 제작자 ENGIceBlasT\n패치 파일을 백업·제거하고 Steam으로 현재 게임 원본을 복구합니다.\nCM 모드는 먼저 비활성화해주세요. 세이브·리플레이·안전 백업은 보존합니다.",Left=24,Top=65,Width=710,Height=85});
        path.SetBounds(24,160,600,28);Controls.Add(path);
        var browse=new Button{Text="폴더 선택",Left=636,Top=157,Width=100,Height=33};Controls.Add(browse);
        browse.Click+=delegate{if(busy)return;using(var d=new FolderBrowserDialog())if(d.ShowDialog(this)==DialogResult.OK)path.Text=d.SelectedPath;};
        remove.Text="1. 완전 삭제 및 복구 시작";remove.SetBounds(24,207,235,42);Controls.Add(remove);
        steam.Text="Steam 복구 다시 요청";steam.SetBounds(269,207,225,42);Controls.Add(steam);
        verify.Text="2. 원본 복구 완료 확인";verify.SetBounds(504,207,232,42);Controls.Add(verify);
        log.SetBounds(24,267,712,215);log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;Controls.Add(log);
        log.Text="Steam 다운로드가 필요할 수 있습니다. 원본 복구 완료 전에는 게임을 실행하지 마세요.";
        remove.Click+=async delegate {
            if(MessageBox.Show(this,"선택 폴더:\n"+path.Text+"\n\n한글패치가 사용한 파일을 백업 후 제거합니다.\n동일 파일을 수정한 다른 모드도 제거되므로 CM 모드를 먼저 비활성화해주세요.\nSteam 무결성 검사가 끝난 뒤 [원본 복구 완료 확인]을 눌러주세요.",Text,MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
            await Work(false);
        };
        verify.Click+=async delegate{await Work(true);};
        steam.Click+=delegate{try{Recovery.RequestSteam();Add("Steam 복구 요청을 전달했습니다. 완료 여부는 별도로 확인해주세요.");}catch(Exception e){Add("Steam 요청 실패: "+e.Message);}};
        Shown+=delegate{try{var games=SteamLocator.Detect();if(games.Count>0)path.Text=games[0].GameDir;}catch(Exception e){Add(e.Message);}};
        FormClosing+=delegate(object s,FormClosingEventArgs e){e.Cancel=busy;};
    }
    void Add(string s){log.AppendText(Environment.NewLine+s+Environment.NewLine);}
    async Task Work(bool check) {
        busy=true;remove.Enabled=verify.Enabled=steam.Enabled=path.Enabled=false;string root=path.Text.Trim();
        try {
            if(check) Add(await Task.Run(()=>Recovery.Verify(root)));
            else {
                string backup=await Task.Run(()=>Recovery.Remove(root,s=>BeginInvoke((MethodInvoker)delegate{Add(s);})));
                Add("백업: "+backup+"\n상태: Steam 원본 복구 대기");
                try{Recovery.RequestSteam();Add("Steam 복구를 요청했습니다. Steam 검사 완료 후 [원본 복구 완료 확인]을 눌러주세요.");}
                catch(Exception e){Add("Steam 실행 요청 실패: "+e.Message+"\n[Steam 복구 다시 요청] 또는 Steam 속성 → 설치된 파일 → 무결성 검사를 이용해주세요.");}
            }
        }catch(Exception e){Add(e.Message);MessageBox.Show(this,e.Message,"작업 상태 확인",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally{busy=false;remove.Enabled=verify.Enabled=steam.Enabled=path.Enabled=true;}
    }
}
internal static class Program {
    [STAThread] static int Main(string[] args) {
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling",false);AppContext.SetSwitch("Switch.System.IO.BlockLongPaths",false);
#if STOCK_RESTORE_TEST
        try {
            if(args.Length==2&&args[0]=="--manifest"){Console.WriteLine(DepotVerifier.Parse(args[1]).Count);return 0;}
            if(args.Length==2&&args[0]=="--remove"){Console.WriteLine(Recovery.Remove(args[1],Console.WriteLine));return 0;}
            if(args.Length==2&&args[0]=="--verify"){Console.WriteLine(Recovery.Verify(args[1]));return 0;}
            if(args.Length==2&&args[0]=="--render"){
                Application.EnableVisualStyles();using(var f=new RecoveryForm()){f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.Show();Application.DoEvents();using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(args[1]);}f.Close();}return 0;
            }
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
#endif
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new RecoveryForm());return 0;
    }
}
}
