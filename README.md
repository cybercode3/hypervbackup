# HyperVBackup

HyperVBackup is, as you can guess, a utility that can perform backups of Hyper-V virtual machines. It uses Volume Shadow Copy Service (VSS), so it can back up running virtual machines.

It started as a fork of http://hypervbackup.codeplex.com, as it was evident that the original creators were not going to update it anymore. So, we added some features that we and the community needed.

Beware that this project is a heavily modified version of the original source code, with many features added (7zip format support, individual file filters, updated to .NET Framework 4.8, etc.). If you want a version closer to the original code, visit http://hypervbackup.codeplex.com/discussions/567463.

Note: Version 4 includes many breaking changes. If you are still using version 2, you can find the previous documentation in the Wiki (https://github.com/ColiseoSoftware/hypervbackup/wiki).

You can see all options by executing the program without arguments. Currently, those are:

```
f, file              Text file containing a list of VMs to backup, one per line.
l, list              List of VMs to backup, comma separated.
x, exclude           List of VMs to exclude from backup, comma seperated.
v, vhdinclude        List of VHDs file names to backup, comma separated.
i, vhdignore         List of VHDs file names to ignore, comma separated.
a, all               (Default: True) Is set, backup all VMs on this server.
n, name              (Default: True) If set, VMs to backup are specified by name.
g, guid              If set, VMs to backup are specified by guid.
o, output            Required. Backup ouput folder.
p, password          Secure the backup with a password.
z, zip               Use the zip format to store the backup.
d, directcopy        Do not compress the output, just copy the files recreating the folder structure.
outputformat         Backup archive name format. {0} is the VM's name, {1} the VM's GUID, {2} is the current date and time and {3} is the extension for the compression format (7z or zip).
                     Default: "{0}_{2:yyyyMMddHHmmss}{3}"
s, singlevss         Perform one single snapshot for all the VMs.
compressionlevel     (Default: 3) Compression level, between 0 (no compression, very fast) and 9 (max. compression, very slow).
cleanoutputbydays    (Default: 0) Delete all files in the output folder older than x days. TOTALLY OPTIONAL. USE WITH CAUTION.
cleanoutputbymb      (Default: 0) Delete older files in the output folder if total size is bigger then x Megabytes. TOTALLY OPTIONAL. USE WITH CAUTION.
onsuccess            Execute this program if backup completes correctly. You must provide a full path to an executable file.
onfailure            Execute this program if backup fails. You must provide a full path to an executable file.
mt                   (Default: off) Enable multi-threaded compression (only for 7zip format). In multicore processors use all the processing power available. The backups are faster at the cost of high processor usage.
```

For example, if you want to back up the Mail Server virtual machine to the \\shared\backups folder, use:

HyperVBackup -l "Mail Server" -o "\\shared\backups" --compressionlevel 0

Note: short switches use one dash (-), and long switches use two dashes (--). Not all options have a short switch available.

HyperVBackup only works on Hyper-V Server and does not work on Hyper-V Client (Windows 10, 11). Client versions of Windows do not include the necessary OS-level support, so this is a Windows limitation and HyperVBackup cannot do anything about it.

By default, the output is stored in 7zip format (you must provide the 7z.dll file corresponding to the version you want to use; version 25.01 is included).
You can use the zip format for the output. In this case, an internal compression engine is used. Backups take more time, and the resulting files are slightly larger when you use the zip format.

Cluster Shared Volumes are supported.

**How to configure logging:**

The logging functions are based on NLog (http://nlog-project.org/), so you can configure the output using the nlog.config file. The official documentation (https://github.com/NLog/NLog/wiki/Configuration-file) shows you how to build one.

The included nlog.config file writes the output to:
* The console window
* A file located in the logfiles subfolder (check that HyperVBackup has write permissions to this folder)
* A log server, which allows you to monitor the backup in real time from another machine. For security, the default configuration uses a localhost address. You can use Sentinel (https://github.com/pablopioli/Sentinel) to watch the logs.

**Requirements**

* .NET Framework 4.8 (https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
