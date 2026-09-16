# Read-only exception for one explicitly permitted concurrent workspace lab.
# These identities never confer permission to control, adopt, or stop the lab.
function Get-IsolatedLabKernelPath([int]$ProcessId,[long]$ExpectedStartTicks) {
    if ($null -eq ('Oc2TasIsolatedLab.KernelImage' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace Oc2TasIsolatedLab {
    public static class KernelImage {
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int id);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(IntPtr handle,out long creation,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true,ExactSpelling=true)] static extern bool QueryFullProcessImageNameW(IntPtr handle,uint flags,StringBuilder name,ref uint length);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        public static string Read(int id,long ticks) {
            IntPtr handle=OpenProcess(0x1000,false,id);
            if(handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                long creation,exit,kernel,user;
                if(!GetProcessTimes(handle,out creation,out exit,out kernel,out user))throw new Win32Exception(Marshal.GetLastWin32Error());
                if(DateTime.FromFileTimeUtc(creation).Ticks!=ticks)throw new InvalidOperationException("Concurrent lab PID creation time changed.");
                uint length=32768;var path=new StringBuilder((int)length);
                if(!QueryFullProcessImageNameW(handle,0,path,ref length))throw new Win32Exception(Marshal.GetLastWin32Error());
                return path.ToString();
            } finally {CloseHandle(handle);}
        }
    }
}
'@
    }
    return [Oc2TasIsolatedLab.KernelImage]::Read($ProcessId,$ExpectedStartTicks)
}

function Get-VerifiedIsolatedLab([int]$ProcessId,[string]$TaskRoot) {
    $taskLabProcess=Get-Process -Id $ProcessId -ErrorAction Stop
    $taskLabTicks=$taskLabProcess.StartTime.ToUniversalTime().Ticks
    $taskLabPath=Get-IsolatedLabKernelPath -ProcessId $ProcessId -ExpectedStartTicks $taskLabTicks
    $taskExpectedLab=[IO.Path]::GetFullPath((Join-Path $TaskRoot 'lab\runtime\Overcooked2.exe'))
    if (-not [IO.Path]::IsPathFullyQualified($taskLabPath) -or -not [string]::Equals([IO.Path]::GetFullPath($taskLabPath),$taskExpectedLab,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Concurrent Overcooked2 process is not the exact permitted workspace lab/runtime/Overcooked2.exe kernel image.'
    }
    return [pscustomobject]@{ Id=$ProcessId; StartTicks=$taskLabTicks; Path=$taskExpectedLab
        ImagePathSource='QueryFullProcessImageNameW'; ImageSha256=(Get-FileHash -LiteralPath $taskExpectedLab -Algorithm SHA256).Hash.ToLowerInvariant()
        ownership=$false; role='Concurrent isolated lab; observed wall-clock rendering/CPU load only; never controlled or stopped by verification' }
}
