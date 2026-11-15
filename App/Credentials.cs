using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ForeverSocial.Companion;
internal static class Credentials
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Entry {public uint Flags,Type;public string TargetName;public string? Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint BlobSize;public IntPtr Blob;public uint Persist,AttributeCount;public IntPtr Attributes;public string? Alias,UserName;}
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool Write(ref Entry value,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool Read(string target,uint type,uint flags,out IntPtr value);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr value);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool Remove(string target,uint type,uint flags);
    private static string Target(string endpoint,string actor)=>"ForeverSocial/"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(endpoint+"\n"+actor)));
    public static void Save(string endpoint,string actor,string token)
    {
        var bytes=Encoding.UTF8.GetBytes(token);var ptr=Marshal.AllocHGlobal(bytes.Length);
        try{Marshal.Copy(bytes,0,ptr,bytes.Length);var entry=new Entry{Type=1,TargetName=Target(endpoint,actor),BlobSize=(uint)bytes.Length,Blob=ptr,Persist=2,UserName="ForeverSocial"};if(!Write(ref entry,0))throw new Win32Exception(Marshal.GetLastWin32Error());}
        finally{Marshal.Copy(new byte[bytes.Length],0,ptr,bytes.Length);Marshal.FreeHGlobal(ptr);}
    }
    public static string? Load(string endpoint,string actor)
    {
        if(!Read(Target(endpoint,actor),1,0,out var ptr))return null;
        try{var entry=Marshal.PtrToStructure<Entry>(ptr);if(entry.BlobSize>4096)return null;var bytes=new byte[entry.BlobSize];Marshal.Copy(entry.Blob,bytes,0,bytes.Length);return Encoding.UTF8.GetString(bytes);}finally{CredFree(ptr);}
    }
    public static void Delete(string endpoint,string actor)
    {
        if(!Remove(Target(endpoint,actor),1,0) && Marshal.GetLastWin32Error()!=1168)throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
