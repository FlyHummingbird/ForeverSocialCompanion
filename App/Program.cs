using System.Text.Json;
using ForeverSocial.Companion;

internal static class Program
{
    // Windows shell dialogs require an STA entry point, including Release builds.
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
#if DEBUG
        if(args is ["--credential-self-test",var testReport]){
            var target="Test-"+Guid.NewGuid().ToString("N");var secret=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            try{Credentials.Save("https://social.wgfreunde.com",target,secret);if(Credentials.Load("https://social.wgfreunde.com",target)!=secret)throw new InvalidDataException("Credential roundtrip failed");}
            finally{Credentials.Delete("https://social.wgfreunde.com",target);}
            File.WriteAllText(testReport,"{\"ok\":true,\"temporaryCredentialRemoved\":true}");return;
        }
        if(args is ["--render-preview",var previewPath]){
            using var preview=new CompanionWindow();preview.ShowInTaskbar=false;preview.Opacity=0;preview.Show();Application.DoEvents();
            using var bitmap=new Bitmap(preview.Width,preview.Height);preview.DrawToBitmap(bitmap,new Rectangle(Point.Empty,preview.Size));bitmap.Save(previewPath);return;
        }
#endif
        Application.Run(new CompanionWindow());
    }
}

