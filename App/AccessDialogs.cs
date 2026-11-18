internal static class AccessDialogs
{
    private static bool German=>System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="de";
    private static string T(string de,string en)=>German?de:en;
    internal static string? File(IWin32Window owner,bool save)
    {
        using FileDialog picker=save?new SaveFileDialog{FileName="ForeverSocial.fsaccess",DefaultExt="fsaccess",AddExtension=true,OverwritePrompt=true}:new OpenFileDialog{CheckFileExists=true,Multiselect=false};
        picker.Filter="Forever Social (*.fsaccess)|*.fsaccess";picker.Title=T(save?"Zugang verschlüsselt sichern":"Zugang übernehmen",save?"Save encrypted access":"Import access");
        return picker.ShowDialog(owner)==DialogResult.OK?picker.FileName:null;
    }
    internal static string? Password(IWin32Window owner,bool confirm)
    {
        using var form=new Form{Text=T("Passwort für die Zugangsdatei","Access file password"),Size=new Size(540,confirm?290:235),FormBorderStyle=FormBorderStyle.FixedDialog,StartPosition=FormStartPosition.CenterParent,MaximizeBox=false,MinimizeBox=false};
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,AutoSize=true};form.Controls.Add(body);
        body.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(490,0),Text=T("Mindestens 12 Zeichen. Datei und Passwort erlauben Zugriff auf diesen Community-Zugang. Passwort getrennt aufbewahren.","At least 12 characters. The file and password grant access to this community account. Keep the password separately.")});
        var password=new TextBox{UseSystemPasswordChar=true,Dock=DockStyle.Top,MaxLength=256,Name="transferPassword"};body.Controls.Add(password);
        var repeat=new TextBox{UseSystemPasswordChar=true,Dock=DockStyle.Top,MaxLength=256,Name="transferRepeat"};
        if(confirm){body.Controls.Add(new Label{AutoSize=true,Text=T("Passwort wiederholen","Repeat password")});body.Controls.Add(repeat);}
        var actions=new FlowLayoutPanel{AutoSize=true};var ok=new Button{Text=T("Fortfahren","Continue"),AutoSize=true};var cancel=new Button{Text=T("Abbrechen","Cancel"),DialogResult=DialogResult.Cancel,AutoSize=true};actions.Controls.Add(ok);actions.Controls.Add(cancel);body.Controls.Add(actions);
        ok.Click+=(_,_)=>{
            if(password.Text.Length<12 || string.IsNullOrWhiteSpace(password.Text) || confirm && password.Text!=repeat.Text){MessageBox.Show(form,T("Passwortlänge und Wiederholung prüfen.","Check password length and confirmation."),form.Text,MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            form.DialogResult=DialogResult.OK;
        };
        form.AcceptButton=ok;form.CancelButton=cancel;
        return form.ShowDialog(owner)==DialogResult.OK?password.Text:null;
    }
    internal static bool Confirm(IWin32Window owner,string message)=>MessageBox.Show(owner,message,T("Forever Social · Bestätigung","Forever Social · confirmation"),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
}
