[0m
> build · beam-free
[0m
[0m$ [0mgit log --oneline -15
12294bc v1.9.2: docs(firewall): architecture audit and comprehensive 5-button fix plan for global alerts and 0-percent cpu
623a70a v1.9.2: fix(notifications): implement smart deduplication for blacklist alerts and eliminate repeated popups
214302b v1.9.1: feat(signing): add Gms CodeWorks root certificate and update sign-binaries script
26a2716 v1.9.1: release(dist): update website links, publish installer and portable zip v1.9.1 to github releases
cf4b329 v1.9.1: fix(core,ui): eliminate lost-wakeup, decouple cache in save, hoist socket checks and clean compiler warnings
72a1165 v1.9.1: feat(opencode): configure isolated swarm runner with live streaming
cb007bf v1.9.1: docs(release): update documentation, clean workspace, bump version and synchronize opencode swarm models
23e2a90 v1.9.0: feat(core,firewall): eliminate LPE, decouple live cache, implement port/app whitelist, silent pid0, audit localization and remove toolbox
2314d6f v1.8.0: docs(audit): add comprehensive fix plan based on opencode triad audits
010b7b6 v1.8.0: feat(stability,perf): core IPC async broadcast, circuit breaker, 0-thread registry watcher, 60fps async icon cache, zero-io reads
a3155b4 v1.7.6: docs(audit): global multi-agent audit report, action plan and distribution rules
c743001 v1.7.6: fix(update): auto-restart UI in interactive user session after silent update installation
af9d79f v1.7.5: release(stability): 1-hour live test verification, memory and handle leak checks, docs and promo site update
4425b42 v1.7.4: feat(optimization): multi-core scanner pipeline, smart sleep idle engine, tray throttle and zero-alloc network struct keys
6eeff6f v1.7.3: docs(swarm): enforce mandatory 3+ opencode subagents parallelization on every user task
[0m
[0m$ [0mGet-ChildItem -Path . -Recurse -Filter "*.xaml" | Select-Object -First 50; Write-Host "---"; Get-ChildItem -Path . -Recurse -Filter "SnapshotManager.cs"
---
[0m
[0m$ [0mGet-ChildItem -Path . -Recurse -Name | Select-String -Pattern "Snapshot" | Select-Object -First 50
(no output)
[0m
[0m$ [0mGet-ChildItem -Path . | Select-Object Name, Mode
(no output)
[0m
[0m$ [0mGet-ChildItem -Path . -Force; Write-Host "---count---"; (Get-ChildItem -Path . -Recurse -File).Count
---count---
0
[0m
[0m$ [0mGet-Location; Write-Host "---"; Test-Path "D:\Nas_sync\Coding\StartupMonitor"; Get-ChildItem "D:\Nas_sync\Coding\StartupMonitor" | Select-Object -First 20
---
Path                                          
----                                          
D:\Nas_sync\Coding\StartupMonitor\bench_audit3
True

PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\.agents
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : .agents
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : .agents
FullName          : D:\Nas_sync\Coding\StartupMonitor\.agents
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : .agents
CreationTime      : 08.10.2026 18:00:41
CreationTimeUtc   : 08.10.2026 15:00:41
LastAccessTime    : 19.10.2026 0:52:30
LastAccessTimeUtc : 18.10.2026 21:52:30
LastWriteTime     : 12.10.2026 0:44:49
LastWriteTimeUtc  : 11.10.2026 21:44:49
Attributes        : Directory
Mode              : d-----
BaseName          : .agents
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\.github
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : .github
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : .github
FullName          : D:\Nas_sync\Coding\StartupMonitor\.github
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : .github
CreationTime      : 13.10.2026 19:10:06
CreationTimeUtc   : 13.10.2026 16:10:06
LastAccessTime    : 18.10.2026 18:42:14
LastAccessTimeUtc : 18.10.2026 15:42:14
LastWriteTime     : 13.10.2026 19:10:06
LastWriteTimeUtc  : 13.10.2026 16:10:06
Attributes        : Directory
Mode              : d-----
BaseName          : .github
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\.opencode
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : .opencode
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : .opencode
FullName          : D:\Nas_sync\Coding\StartupMonitor\.opencode
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : .opencode
CreationTime      : 08.09.2026 1:06:32
CreationTimeUtc   : 07.09.2026 22:06:32
LastAccessTime    : 19.10.2026 0:52:30
LastAccessTimeUtc : 18.10.2026 21:52:30
LastWriteTime     : 13.10.2026 19:10:06
LastWriteTimeUtc  : 13.10.2026 16:10:06
Attributes        : Directory
Mode              : d-----
BaseName          : .opencode
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\assets
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : assets
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : assets
FullName          : D:\Nas_sync\Coding\StartupMonitor\assets
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 15.09.2026 0:04:27
CreationTimeUtc   : 14.09.2026 21:04:27
LastAccessTime    : 19.10.2026 0:52:30
LastAccessTimeUtc : 18.10.2026 21:52:30
LastWriteTime     : 18.09.2026 1:23:34
LastWriteTimeUtc  : 17.09.2026 22:23:34
Attributes        : Directory
Mode              : d-----
BaseName          : assets
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\bench_audit1
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : bench_audit1
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : bench_audit1
FullName          : D:\Nas_sync\Coding\StartupMonitor\bench_audit1
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 25.10.2026 22:48:18
CreationTimeUtc   : 25.10.2026 19:48:18
LastAccessTime    : 25.10.2026 22:48:18
LastAccessTimeUtc : 25.10.2026 19:48:18
LastWriteTime     : 25.10.2026 22:48:18
LastWriteTimeUtc  : 25.10.2026 19:48:18
Attributes        : Directory
Mode              : d-----
BaseName          : bench_audit1
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\bench_audit2
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : bench_audit2
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : bench_audit2
FullName          : D:\Nas_sync\Coding\StartupMonitor\bench_audit2
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 25.10.2026 22:48:18
CreationTimeUtc   : 25.10.2026 19:48:18
LastAccessTime    : 25.10.2026 22:48:18
LastAccessTimeUtc : 25.10.2026 19:48:18
LastWriteTime     : 25.10.2026 22:48:18
LastWriteTimeUtc  : 25.10.2026 19:48:18
Attributes        : Directory
Mode              : d-----
BaseName          : bench_audit2
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\bench_audit3
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : bench_audit3
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : bench_audit3
FullName          : D:\Nas_sync\Coding\StartupMonitor\bench_audit3
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 25.10.2026 22:48:18
CreationTimeUtc   : 25.10.2026 19:48:18
LastAccessTime    : 25.10.2026 22:48:18
LastAccessTimeUtc : 25.10.2026 19:48:18
LastWriteTime     : 25.10.2026 22:48:18
LastWriteTimeUtc  : 25.10.2026 19:48:18
Attributes        : Directory
Mode              : d-----
BaseName          : bench_audit3
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\docs
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : docs
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : docs
FullName          : D:\Nas_sync\Coding\StartupMonitor\docs
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 08.09.2026 1:07:10
CreationTimeUtc   : 07.09.2026 22:07:10
LastAccessTime    : 25.10.2026 22:50:49
LastAccessTimeUtc : 25.10.2026 19:50:49
LastWriteTime     : 25.10.2026 22:50:49
LastWriteTimeUtc  : 25.10.2026 19:50:49
Attributes        : Directory
Mode              : d-----
BaseName          : docs
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\installers
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : installers
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : installers
FullName          : D:\Nas_sync\Coding\StartupMonitor\installers
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 13.10.2026 19:10:06
CreationTimeUtc   : 13.10.2026 16:10:06
LastAccessTime    : 21.10.2026 0:54:57
LastAccessTimeUtc : 20.10.2026 21:54:57
LastWriteTime     : 21.10.2026 0:54:57
LastWriteTimeUtc  : 20.10.2026 21:54:57
Attributes        : Directory
Mode              : d-----
BaseName          : installers
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\logs
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : logs
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : logs
FullName          : D:\Nas_sync\Coding\StartupMonitor\logs
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 21.10.2026 1:03:51
CreationTimeUtc   : 20.10.2026 22:03:51
LastAccessTime    : 25.10.2026 22:48:18
LastAccessTimeUtc : 25.10.2026 19:48:18
LastWriteTime     : 25.10.2026 22:48:18
LastWriteTimeUtc  : 25.10.2026 19:48:18
Attributes        : Directory
Mode              : d-----
BaseName          : logs
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\Rulez
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : Rulez
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : Rulez
FullName          : D:\Nas_sync\Coding\StartupMonitor\Rulez
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 19.10.2026 18:30:43
CreationTimeUtc   : 19.10.2026 15:30:43
LastAccessTime    : 21.10.2026 18:36:26
LastAccessTimeUtc : 21.10.2026 15:36:26
LastWriteTime     : 21.10.2026 18:36:26
LastWriteTimeUtc  : 21.10.2026 15:36:26
Attributes        : Directory
Mode              : d-----
BaseName          : Rulez
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\scripts
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : scripts
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : scripts
FullName          : D:\Nas_sync\Coding\StartupMonitor\scripts
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 15.09.2026 0:04:27
CreationTimeUtc   : 14.09.2026 21:04:27
LastAccessTime    : 25.10.2026 22:51:50
LastAccessTimeUtc : 25.10.2026 19:51:50
LastWriteTime     : 25.10.2026 22:51:50
LastWriteTimeUtc  : 25.10.2026 19:51:50
Attributes        : Directory
Mode              : d-----
BaseName          : scripts
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\SMon2
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : SMon2
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : SMon2
FullName          : D:\Nas_sync\Coding\StartupMonitor\SMon2
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 08.09.2026 1:07:10
CreationTimeUtc   : 07.09.2026 22:07:10
LastAccessTime    : 18.10.2026 18:42:16
LastAccessTimeUtc : 18.10.2026 15:42:16
LastWriteTime     : 16.10.2026 1:53:15
LastWriteTimeUtc  : 15.10.2026 22:53:15
Attributes        : Directory
Mode              : d-----
BaseName          : SMon2
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\src
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : src
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : src
FullName          : D:\Nas_sync\Coding\StartupMonitor\src
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 08.09.2026 1:07:10
CreationTimeUtc   : 07.09.2026 22:07:10
LastAccessTime    : 19.10.2026 0:52:31
LastAccessTimeUtc : 18.10.2026 21:52:31
LastWriteTime     : 08.09.2026 1:07:12
LastWriteTimeUtc  : 07.09.2026 22:07:12
Attributes        : Directory
Mode              : d-----
BaseName          : src
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\tests
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : tests
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : tests
FullName          : D:\Nas_sync\Coding\StartupMonitor\tests
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 08.09.2026 1:07:16
CreationTimeUtc   : 07.09.2026 22:07:16
LastAccessTime    : 19.10.2026 0:52:31
LastAccessTimeUtc : 18.10.2026 21:52:31
LastWriteTime     : 08.09.2026 1:07:16
LastWriteTimeUtc  : 07.09.2026 22:07:16
Attributes        : Directory
Mode              : d-----
BaseName          : tests
Target            : {}
LinkType          : 


PSPath            : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor\www
PSParentPath      : Microsoft.PowerShell.Core\FileSystem::D:\Nas_sync\Coding\StartupMonitor
PSChildName       : www
PSDrive           : D
PSProvider        : Microsoft.PowerShell.Core\FileSystem
PSIsContainer     : True
Name              : www
FullName          : D:\Nas_sync\Coding\StartupMonitor\www
Parent            : StartupMonitor
Exists            : True
Root              : D:\
Extension         : 
CreationTime      : 11.10.2026 13:42:38
CreationTimeUtc   : 11.10.2026 10:42:38
LastAccessTime    : 21.10.2026 18:36:26
LastAccessTimeUtc : 21.10.2026 15:36:26
LastWriteTime     : 21.10.2026 18:36:26
LastWriteTimeUtc  : 21.10.2026 15:36:26
Attributes        : Directory
Mode              : d-----
BaseName          : www
Target            : {}
LinkType          : 


LastWriteTime : 21.10.2026 0:54:57
Length        : 619
Name          : .gitignore


LastWriteTime : 17.10.2026 1:32:21
Length        : 499
Name          : .stignore


LastWriteTime : 25.10.2026 3:09:07
Length        : 14655
Name          : AGENTS.md


LastWriteTime : 23.10.2026 16:39:58
Length        : 23004
Name          : AUDIT_REPORT_v1.7.6.md
[0m
[0m$ [0mGet-ChildItem -Path "D:\Nas_sync\Coding\StartupMonitor\src" -Recurse -Filter "*.xaml" | Select-Object FullName; Write-Host "---CS---"; Get-ChildItem -Path "D:\Nas_sync\Coding\StartupMonitor\src" -Recurse -Filter "Snapshot*.cs" | Select-Object FullName
---CS---
FullName                                                                                                               
--------                                                                                                               
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\App.xaml                                                       
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Styles\ButtonStyles.xaml                                       
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Styles\CardStyles.xaml                                         
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Styles\Colors.xaml                                             
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Styles\DataGridStyles.xaml                                     
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Styles\Typography.xaml                                         
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Styles\VectorIcons.xaml                                        
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml                                          
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\NetworkSecurityHubView.xaml                              
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\EntryDetailWindow.xaml                           
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\FirewallRuleEditWindow.xaml                      
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\NetworkConnectionAlertWindow.xaml                
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\PortDefenderConfigWindow.xaml                    
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\ProcessConnectionsWindow.xaml                    
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\ProcessModulesWindow.xaml                        
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\SnapshotsWindow.xaml                             
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\SystemInfoWindow.xaml                            
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\UpdateAvailableWindow.xaml                       
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SnapshotManager.cs                               
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\Snapshot\SnapshotComparator.cs                      
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\Snapshot\SnapshotComparisonResult.cs                
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\Snapshot\SnapshotModifiedEntryItem.cs               
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\obj\Debug\net8.0-windows10.0.17763\win-x64\Views\Dialogs\Sna...
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\obj\pub_ui\Views\Dialogs\SnapshotsWindow.g.cs                  
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\obj\Release\net8.0-windows10.0.17763\win-x64\Views\Dialogs\S...
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\SnapshotsWindow.xaml.cs
[0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/MainWindow.xaml
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/MainWindow.xaml.cs
[0m✱ [0mGrep "Snapshot"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI · 100 matches[0m
[0m✱ [0mGrep "Snapshot|OpenSnapshots|IsSnapshotComparison|ActiveSnapshotComparison|ResetSnapshotComparison|SnapshotCount|SavedSnapshotsCount"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/ViewModels/MainViewModel.cs · 70 matches[0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/ViewModels/MainViewModel.cs[90m [offset=1920, limit=200][0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.Service/Services/SnapshotManager.cs
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/Dialogs/SnapshotsWindow.xaml
[0m
[0m$ [0mGet-ChildItem "D:\Nas_sync\Coding\StartupMonitor\bench_audit3"; Write-Host "==="; Get-ChildItem "D:\Nas_sync\Coding\StartupMonitor\bench_audit1" | Select-Object -First 20; Write-Host "==="; Get-ChildItem "D:\Nas_sync\Coding\StartupMonitor\bench_audit2" | Select-Object -First 20
===
===
[0m
[0m$ [0mTest-Path "D:\Nas_sync\Coding\StartupMonitor\bench_audit3\.git"; Get-Location; git -C "D:\Nas_sync\Coding\StartupMonitor" status --short | Select-Object -First 20
False

Path                                          
----                                          
D:\Nas_sync\Coding\StartupMonitor\bench_audit3
 M AGENTS.md
 M Directory.Build.props
 M GEMINI.md
 M README.md
 M Rulez/AGENTS.md
 M Rulez/GEMINI.md
 M docs/FIREWALL_FIX_PLAN.md
 M docs/ROADMAP.md
 M opencode.json
 M scripts/sign-binaries.ps1
 M src/StartupMonitor.Service/Services/BlacklistManager.cs
 M src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs
 M src/StartupMonitor.Service/Services/OtherStartupLocationsWatcher.cs
 M src/StartupMonitor.Service/Services/TaskSchedulerWatcher.cs
 M src/StartupMonitor.Service/Services/Update/GitHubUpdateService.cs
 M src/StartupMonitor.Service/Services/WatcherHelper.cs
 M src/StartupMonitor.Service/Services/WindowsFirewallService.cs
 M src/StartupMonitor.Service/Services/WmiSubscriptionWatcher.cs
 M src/StartupMonitor.Shared/Helpers/CpuUsageHelper.cs
 M src/StartupMonitor.Shared/Models/AppConfig.cs
[0m
[0m✱ [0mGrep "IconCamera|IconHome|Camera24"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Styles/VectorIcons.xaml · 2 matches[0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/Dialogs/SnapshotsWindow.xaml.cs
[0m✱ [0mGrep "GetSnapshotsAsync|CreateSnapshotAsync|DeleteSnapshotAsync|CompareSnapshotAsync"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Services/PipeClient.cs · 4 matches[0m
[0m✱ [0mGrep "NavSnapshots|NavBootTime|NavFirewall"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Resources/Strings.ru.resx · 4 matches[0m
[0m
[0m✗ [0mInvalid Tool
The arguments provided to the tool are invalid: Model tried to call unavailable tool 'sed'. Available tools: bash, edit, glob, grep, invalid, read, skill, task, todowrite, webfetch, websearch, write.
[0m
[0m$ [0msed -n '928,940p' "D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.ru.resx"
sed : ��� "sed" �� �ᯮ����� ��� ��� ���������, �㭪樨, 䠩�� �業��� ��� �믮��塞�� �ணࠬ��. �஢���� �ࠢ���
���� ����ᠭ�� �����, � ⠪�� ����稥 � �ࠢ��쭮��� ���, ��᫥ 祣� ������ ������.
��ப�:1 ����:1
+ sed -n '928,940p' "D:\Nas_sync\Coding\StartupMonitor\src\StartupMonit ...
+ ~~~
    + CategoryInfo          : ObjectNotFound: (sed:String) [], CommandNotFoundException
    + FullyQualifiedErrorId : CommandNotFoundException
[0m
[0m$ [0mpowershell -Command "Get-Content 'D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.ru.resx' | Select-Object -Index (930..936)"
</data>
  <data name="NavBootTime" xml:space="preserve">
    <value>�-���?�?�?�����</value>
  </data>
  <data name="BootTimeTitle" xml:space="preserve">
    <value>�?�?���>������'�?�? �?�?��?��?�� �����?�?�?����� Windows</value>
  </data>
[0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Resources/Strings.ru.resx[90m [offset=930, limit=6][0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Resources/Strings.resx[90m [offset=932, limit=4][0m
[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Resources/Strings.resx
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.resx
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.resx
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.resx
@@ -931,8 +931,11 @@
 </data>
 <data name="NavBootTime" xml:space="preserve">
   <value>Boot Time</value>
 </data>
+<data name="NavSnapshots" xml:space="preserve">
+  <value>Snapshots</value>
+</data>
 <data name="BootTimeTitle" xml:space="preserve">
   <value>Windows Boot Time Analyzer</value>
 </data>
 <data name="BootTimeSubtitle" xml:space="preserve">

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Resources/Strings.ru.resx
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.ru.resx
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.ru.resx
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Resources\Strings.ru.resx
@@ -931,8 +931,11 @@
 </data>
 <data name="NavBootTime" xml:space="preserve">
   <value>Загрузка</value>
 </data>
+<data name="NavSnapshots" xml:space="preserve">
+  <value>Снимки системы</value>
+</data>
 <data name="BootTimeTitle" xml:space="preserve">
   <value>Анализатор времени загрузки Windows</value>
 </data>
 <data name="BootTimeSubtitle" xml:space="preserve">

[0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Services/LocalizationService.cs[90m [offset=590, limit=20][0m
[0m✱ [0mGrep "NavBootTime|NavFirewall|NavHistory"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Services/LocalizationService.cs · 3 matches[0m
[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Services/LocalizationService.cs
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Services\LocalizationService.cs
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Services\LocalizationService.cs
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Services\LocalizationService.cs
@@ -633,8 +633,10 @@
 
 // ──── Boot Time Tracer ────
 /// <summary>Название пункта навигации «Загрузка» (Boot Time).</summary>
 public string NavBootTime => this["NavBootTime"];
+/// <summary>Пункт навигации: снимки системы.</summary>
+public string NavSnapshots => this["NavSnapshots"];
 /// <summary>Заголовок страницы анализатора времени загрузки.</summary>
 public string BootTimeTitle => this["BootTimeTitle"];
 /// <summary>Подзаголовок страницы анализатора времени загрузки.</summary>
 public string BootTimeSubtitle => this["BootTimeSubtitle"];

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/ViewModels/MainViewModel.cs
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
@@ -1945,8 +1945,12 @@
 /// <summary>Текст информации о результатах сравнения со снимком.</summary>
 [ObservableProperty]
 private string? _activeSnapshotComparisonText;
 
+/// <summary>Количество сохранённых контрольных снимков системы (для бейджа в навигации).</summary>
+[ObservableProperty]
+private int _savedSnapshotsCount;
+
 /// <summary>Список Id новых записей со снимка.</summary>
 public HashSet<string> SnapshotAddedEntryIds { get; } = new(StringComparer.OrdinalIgnoreCase);
 
 /// <summary>Словарь Id измененных записей и краткой сводки изменений со снимка.</summary>

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/ViewModels/MainViewModel.cs
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs
@@ -1981,8 +1981,22 @@
 {
     OpenSnapshotsRequested?.Invoke();
 }
 
+/// <summary>Обновляет счётчик сохранённых снимков через службу (для бейджа в левой навигации).</summary>
+public async Task RefreshSavedSnapshotsCountAsync()
+{
+    try
+    {
+        var snapshots = await _pipeClient.GetSnapshotsAsync();
+        SavedSnapshotsCount = snapshots?.Count ?? 0;
+    }
+    catch
+    {
+        // Счётчик некритичен: при недоступности службы оставляем предыдущее значение.
+    }
+}
+
 /// <summary>Сбрасывает режим сравнения со снимком, очищая бейджи и восстанавливая стандартную фильтрацию.</summary>
 [RelayCommand]
 public void ResetSnapshotComparison()
 {

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/Dialogs/SnapshotsWindow.xaml.cs
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\SnapshotsWindow.xaml.cs
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\SnapshotsWindow.xaml.cs
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\SnapshotsWindow.xaml.cs
@@ -33,8 +33,10 @@
 {
     var snapshots = await _pipeClient.GetSnapshotsAsync();
     GridSnapshots.ItemsSource = snapshots;
     TxtNoSnapshots.Visibility = (snapshots.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
+    // Обновляем счётчик сохранённых снимков в бейдже левой навигации главного окна
+    _vm.SavedSnapshotsCount = snapshots.Count;
 }
 catch (Exception ex)
 {
     MessageBox.Show($"Ошибка загрузки снимков: {ex.Message}", "Startup Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/MainWindow.xaml
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml
@@ -74,20 +74,37 @@
 </StackPanel>
 
 <!-- Навигация -->
 <StackPanel Grid.Row="1" Margin="8 0">
-     <Border x:Name="NavItem0" Tag="0" MouseLeftButtonDown="OnNavClick" Style="{StaticResource NavItemStyle}">
-         <Grid>
-             <Rectangle x:Name="NavBar0" Width="3.5" Height="22" HorizontalAlignment="Left" RadiusX="1.75" RadiusY="1.75"
-                        Fill="{StaticResource NavActiveMarkerBrush}" Visibility="Collapsed" />
-             <StackPanel Orientation="Horizontal" Margin="6 0 0 0">
-                 <Viewbox Width="16" Height="16">
-                     <Path Data="{StaticResource IconHome}" Fill="{ui:ThemeResource TextFillColorPrimaryBrush}" Stretch="Uniform" />
-                 </Viewbox>
-                 <TextBlock Text="{Binding NavOverview, Source={StaticResource Loc}}" Style="{StaticResource NavTextStyle}" Margin="10 0 0 0" />
-             </StackPanel>
-         </Grid>
-     </Border>
+      <Border x:Name="NavItem0" Tag="0" MouseLeftButtonDown="OnNavClick" Style="{StaticResource NavItemStyle}">
+          <Grid>
+              <Rectangle x:Name="NavBar0" Width="3.5" Height="22" HorizontalAlignment="Left" RadiusX="1.75" RadiusY="1.75"
+                         Fill="{StaticResource NavActiveMarkerBrush}" Visibility="Collapsed" />
+              <StackPanel Orientation="Horizontal" Margin="6 0 0 0">
+                  <Viewbox Width="16" Height="16">
+                      <Path Data="{StaticResource IconHome}" Fill="{ui:ThemeResource TextFillColorPrimaryBrush}" Stretch="Uniform" />
+                  </Viewbox>
+                  <TextBlock Text="{Binding NavOverview, Source={StaticResource Loc}}" Style="{StaticResource NavTextStyle}" Margin="10 0 0 0" />
+              </StackPanel>
+          </Grid>
+      </Border>
+      <!-- Пункт навигации: Снимки системы (контрольные снимки «Было — Стало») -->
+      <Border x:Name="NavItemSnapshots" Tag="Snapshots" MouseLeftButtonDown="OnNavClick" Style="{StaticResource NavItemStyle}">
+          <Grid>
+              <StackPanel Orientation="Horizontal" Margin="6 0 0 0">
+                  <Viewbox Width="16" Height="16">
+                      <Path Data="{StaticResource IconCamera}" Fill="{ui:ThemeResource TextFillColorPrimaryBrush}" Stretch="Uniform" />
+                  </Viewbox>
+                  <TextBlock Text="{Binding NavSnapshots, Source={StaticResource Loc}}" Style="{StaticResource NavTextStyle}" Margin="10 0 0 0" />
+                  <!-- Счётчик сохранённых снимков -->
+                  <Border Margin="8 0 0 0" Padding="6 1" CornerRadius="8" Background="{DynamicResource BadgeBluePill}"
+                          Visibility="{Binding SavedSnapshotsCount, Converter={StaticResource IntGreaterThanZeroToVisibilityConverter}}">
+                      <TextBlock Text="{Binding SavedSnapshotsCount}" FontSize="10.5" FontWeight="700"
+                                 Foreground="{DynamicResource BadgeBlueTextBrush}" />
+                  </Border>
+              </StackPanel>
+          </Grid>
+      </Border>
      <Border x:Name="NavItem6" Tag="6" MouseLeftButtonDown="OnNavClick" Style="{StaticResource NavItemStyle}">
          <Grid>
              <Rectangle x:Name="NavBar6" Width="3.5" Height="22" HorizontalAlignment="Left" RadiusX="1.75" RadiusY="1.75"
                         Fill="{StaticResource NavActiveMarkerBrush}" Visibility="Collapsed" />

[0m
[0m✱ [0mGrep "IntGreaterThanZero|GreaterThanZero|Converter.*Visibility"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI · 48 matches[0m
[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/MainWindow.xaml
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml
@@ -95,10 +95,9 @@
         <Path Data="{StaticResource IconCamera}" Fill="{ui:ThemeResource TextFillColorPrimaryBrush}" Stretch="Uniform" />
     </Viewbox>
     <TextBlock Text="{Binding NavSnapshots, Source={StaticResource Loc}}" Style="{StaticResource NavTextStyle}" Margin="10 0 0 0" />
     <!-- Счётчик сохранённых снимков -->
-    <Border Margin="8 0 0 0" Padding="6 1" CornerRadius="8" Background="{DynamicResource BadgeBluePill}"
-            Visibility="{Binding SavedSnapshotsCount, Converter={StaticResource IntGreaterThanZeroToVisibilityConverter}}">
+    <Border Margin="8 0 0 0" Padding="6 1" CornerRadius="8" Background="{DynamicResource BadgeBluePill}">
         <TextBlock Text="{Binding SavedSnapshotsCount}" FontSize="10.5" FontWeight="700"
                    Foreground="{DynamicResource BadgeBlueTextBrush}" />
     </Border>
 </StackPanel>

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/MainWindow.xaml.cs
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
@@ -602,10 +602,20 @@
 
 /// <summary>Обрабатывает клик по элементу навигации для переключения страниц.</summary>
 private void OnNavClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
 {
-    if (sender is FrameworkElement fe && fe.Tag is string tag && int.TryParse(tag, out var idx))
-        NavigateToPage(idx);
+    if (sender is FrameworkElement fe && fe.Tag is string tag)
+    {
+        // Числовые теги — переключение страниц; «Snapshots» — открытие диалога снимков системы.
+        if (int.TryParse(tag, out var idx))
+        {
+            NavigateToPage(idx);
+        }
+        else if (tag == "Snapshots")
+        {
+            _viewModel.OpenSnapshotsCommand.Execute(null);
+        }
+    }
 }
 
 /// <summary>Обрабатывает клик по кнопке выхода из приложения.</summary>
 private void OnExitClick(object sender, System.Windows.Input.MouseButtonEventArgs e)

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/MainWindow.xaml.cs
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
@@ -114,8 +114,11 @@
 await StartupSequence();
 
 _isStartupComplete = true;
 
+// Обновляем счётчик сохранённых снимков для бейджа в левой навигации
+_ = _viewModel.RefreshSavedSnapshotsCountAsync();
+
 if (savedTab != 0)
 {
     // Для вкладок, требующих соединения со службой, обновляем данные после инициализации pipe
     if (savedTab == 3) _ = _viewModel.LoadRemovedEntriesAsync();

[0m
[0m← [0mEdit D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Views/MainWindow.xaml.cs
Index: D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
===================================================================
--- D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
+++ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs
@@ -180,8 +180,10 @@
 {
     App.Log("MainWindow: Opening SnapshotsWindow");
     var win = new Dialogs.SnapshotsWindow(_pipeClient, _viewModel) { Owner = this };
     win.ShowDialog();
+    // После закрытия диалога обновляем счётчик сохранённых снимков
+    _ = _viewModel.RefreshSavedSnapshotsCountAsync();
 }
 catch (Exception ex)
 {
     App.Log($"MainWindow: Error opening SnapshotsWindow: {ex}");

[0m
[0m✱ [0mGrep "GetSnapshotActiveComparison"[90m in D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Services/LocalizationService.cs · 1 match[0m
[0m→ [0mRead D:/Nas_sync/Coding/StartupMonitor/src/StartupMonitor.UI/Services/LocalizationService.cs[90m [offset=725, limit=5][0m
[0m
[0m$ [0mGet-ChildItem "D:\Nas_sync\Coding\StartupMonitor\src" -Recurse -Filter "*.csproj" | Select-Object FullName
FullName                                                                                  
--------                                                                                  
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\StartupMonitor.Service.csproj
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.csproj  
D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj
[0m
[0m$ [0mdotnet build "D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj" 2>&1 | Select-Object -Last 40
Определение проектов для восстановления...
  Все проекты обновлены для восстановления.
  StartupMonitor.Shared -> D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
  StartupMonitor.UI -> D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\bin\Debug\net8.0-windows10.0.17763\win-x64\StartupMonitor.UI.dll
    ╨Ю╨┐╤А╨╡╨┤╨╡╨╗╨╡╨╜╨╕╨╡ ╨┐╤А╨╛╨╡╨║╤В╨╛╨▓ ╨┤╨╗╤П ╨▓╨╛╤Б╤Б╤В╨░╨╜╨╛╨▓╨╗╨╡╨╜╨╕╤П...
    ╨Т╨╛╤Б╤Б╤В╨░╨╜╨╛╨▓╨╗╨╡╨╜ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\StartupMonitor.Shared.csproj (╨╖╨░ 634┬а╨╝╤Б).
    ╨Т╨╛╤Б╤Б╤В╨░╨╜╨╛╨▓╨╗╨╡╨╜ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\StartupMonitor.Service.csproj (╨╖╨░ 705┬а╨╝╤Б).
    StartupMonitor.Shared -> D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
    StartupMonitor.Service -> D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\bin\Debug\net8.0\win-x64\StartupMonitor.Service.dll
    StartupMonitor.Service -> D:\Nas_sync\Coding\StartupMonitor\SMon2\
    ╨Ю╨┐╤А╨╡╨┤╨╡╨╗╨╡╨╜╨╕╨╡ ╨┐╤А╨╛╨╡╨║╤В╨╛╨▓ ╨┤╨╗╤П ╨▓╨╛╤Б╤Б╤В╨░╨╜╨╛╨▓╨╗╨╡╨╜╨╕╤П...
    ╨Т╨╛╤Б╤Б╤В╨░╨╜╨╛╨▓╨╗╨╡╨╜ D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\StartupMonitor.UI.csproj (╨╖╨░ 811┬а╨╝╤Б).
    ╨Я╤А╨╛╨╡╨║╤В╨╛╨▓ ╨╛╨▒╨╜╨╛╨▓╨╗╨╡╨╜╨╛ ╨┤╨╗╤П ╨▓╨╛╤Б╤Б╤В╨░╨╜╨╛╨▓╨╗╨╡╨╜╨╕╤П: 1 ╨╕╨╖ 2.
    StartupMonitor.Shared -> D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\bin\Debug\net8.0\StartupMonitor.Shared.dll
    StartupMonitor.UI -> D:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\bin\Debug\net8.0-windows10.0.17763\win-x64\StartupMonitor.UI.dll
    StartupMonitor.UI -> D:\Nas_sync\Coding\StartupMonitor\SMon2\

Сборка успешно завершена.
    Предупреждений: 0
    Ошибок: 0

Прошло времени 00:00:54.58
[0m
Добавлен пункт навигации и счётчик; баннер «Было — Стало» привязан к `IsSnapshotComparisonActive` и работает. Сборка успешна (0 ошибок).

**XAML — левое меню (MainWindow.xaml):**
```xml
<!-- Пункт навигации: Снимки системы -->
<Border x:Name="NavItemSnapshots" Tag="Snapshots" MouseLeftButtonDown="OnNavClick" Style="{StaticResource NavItemStyle}">
    <Grid>
        <StackPanel Orientation="Horizontal" Margin="6 0 0 0">
            <Viewbox Width="16" Height="16">
                <Path Data="{StaticResource IconCamera}" Fill="{ui:ThemeResource TextFillColorPrimaryBrush}" Stretch="Uniform" />
            </Viewbox>
            <TextBlock Text="{Binding NavSnapshots, Source={StaticResource Loc}}" Style="{StaticResource NavTextStyle}" Margin="10 0 0 0" />
            <Border Margin="8 0 0 0" Padding="6 1" CornerRadius="8" Background="{DynamicResource BadgeBluePill}">
                <TextBlock Text="{Binding SavedSnapshotsCount}" FontSize="10.5" FontWeight="700" Foreground="{DynamicResource BadgeBlueTextBrush}" />
            </Border>
        </StackPanel>
    </Grid>
</Border>
```

**C# — обработчик (MainWindow.xaml.cs):**
```csharp
private void OnNavClick(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement fe && fe.Tag is string tag)
    {
        if (int.TryParse(tag, out var idx)) NavigateToPage(idx);
        else if (tag == "Snapshots") _viewModel.OpenSnapshotsCommand.Execute(null);
    }
}
```

**C# — MainViewModel:**
```csharp
[ObservableProperty] private int _savedSnapshotsCount;

public async Task RefreshSavedSnapshotsCountAsync()
{
    try { var s = await _pipeClient.GetSnapshotsAsync(); SavedSnapshotsCount = s?.Count ?? 0; }
    catch { /* счётчик некритичен */ }
}
```

**Баннер (MainWindow.xaml, проверено):**
```xml
<Border Visibility="{Binding IsSnapshotComparisonActive, Converter={StaticResource BooleanToVisibilityConverter}}" ...>
    <TextBlock Text="{Binding ActiveSnapshotComparisonText}" ... />
    <ui:Button Command="{Binding ResetSnapshotComparisonCommand}" ... />
</Border>
```
