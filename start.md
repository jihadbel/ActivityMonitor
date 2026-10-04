أنت تعمل الآن داخل Visual Studio Solution موجود بالفعل باسم:

ActivityMonitor

وقد تم إنشاء المشاريع التالية مسبقًا باستخدام .NET 10:

ActivityMonitor.Core
ActivityMonitor.Infrastructure
ActivityMonitor.Agent
ActivityMonitor.Service
ActivityMonitor.Reporting
ActivityMonitor.Tests


أريد منك تولي المشروع بالكامل من هذه النقطة.

لا تطلب مني إضافة Project References أو NuGet Packages أو إنشاء مجلدات أو تعديل ملفات يدويًا. قم أنت بكل ذلك.

افحص أولًا جميع ملفات Solution وProjects الموجودة، ثم أصلح أي إعدادات أو References غير صحيحة.

لا تعِد إنشاء Solution أو المشاريع الموجودة إلا إذا كان هناك سبب تقني حقيقي، وفي هذه الحالة اشرح السبب قبل القيام بتغيير جذري.

==================================================
الهدف
==================================================

بناء برنامج Windows باسم ActivityMonitor باستخدام C# و.NET 10.

البرنامج مخصص لجهازي الشخصي، وأنا من يوافق على ذلك . و اريد البرنامج ان يعمل في صمت تام بدون ان يزعجني في عملي 

البرنامج يجب أن:

1. يعمل تلقائيًا مع Windows.
2. يسجل نشاط المستخدم.
3. يعرف البرنامج/النافذة التي يعمل عليها المستخدم.
4. يسجل أوقات بداية ونهاية النشاط.
5.معرفة الملفات التي افتحها و مسارها و كل شيء في الويندوز اكسبلورر
6. يكتشف Idle / Lock / Unlock / Logon / Logoff قدر الإمكان.
7. يسجل الشاشة فيديو MP4.
8. يقسم الفيديو إلى مقاطع زمنية.
9. يربط كل ActivityEvent بمقطع الفيديو المقابل والتوقيت داخله.
10. يخزن البيانات الأساسية في SQLite.
11. يصدر تقارير Excel وCSV/TXT للمراجعة.
12. يدير مساحة التخزين وحذف التسجيلات القديمة حسب Policy.
13. يعمل باستقرار واستهلاك منخفض للموارد.
14. يعمل محليًا بالكامل .

لا يوجد في هذه النسخة:

Backend
Cloud
React
Web Dashboard
Remote Monitoring

==================================================
قواعد مهمة أثناء التنفيذ
==================================================

أريد منك تنفيذ المشروع فعليًا، وليس فقط شرح ما يجب فعله.

لديك صلاحية:

- تعديل ملفات csproj.
- إضافة Project References.
- تثبيت NuGet Packages.
- إنشاء المجلدات.
- إنشاء Classes وInterfaces وEntities.
- إنشاء Migrations عند الحاجة.
- تشغيل dotnet restore.
- تشغيل dotnet build.
- إصلاح الأخطاء التي تظهر.
- إعادة تنظيم الملفات إذا احتاج التصميم لذلك.

لا تطلب مني تنفيذ أوامر تستطيع تنفيذها بنفسك.

إذا ظهر Compiler Error، قم بتحليله وإصلاحه.

بعد كل مرحلة مهمة شغّل Build.

لا تعتبر المرحلة ناجحة إذا كان Build يفشل.

استخدم أحدث APIs مناسبة لـ.NET 10 وWindows الحديث وتجنب الحلول القديمة دون سبب.

==================================================
Architecture
==================================================

استخدم المشاريع الحالية بالشكل المناسب:

ActivityMonitor.Core
ActivityMonitor.Infrastructure
ActivityMonitor.Agent
ActivityMonitor.Service
ActivityMonitor.Reporting
ActivityMonitor.Tests

اضبط Project References بنفسك بطريقة تمنع Circular Dependencies.

اجعل Core أقل مشروع اعتمادًا على المشاريع الأخرى.

استخدم:

Dependency Injection
Microsoft.Extensions.Hosting
Microsoft.Extensions.Logging
Options Pattern
Entity Framework Core
SQLite
async/await
CancellationToken
Structured Logging

طبق Separation of Concerns.

==================================================
ActivityMonitor.Core
==================================================

ضع فيه Domain Models وInterfaces والعقود المشتركة.

أنشئ Entity مناسبة باسم:

ActivityEvent

وتحتوي مبدئيًا على:

Id
MachineId
SessionId
UserIdentifier
ProcessName
ProcessId
ProcessPath
WindowTitle
ContextPath
EventType
StartedAtUtc
EndedAtUtc
DurationSeconds
RecordingSegmentId nullable
CreatedAtUtc

أنشئ:

RecordingSegment

ويحتوي على:

Id
MachineId
SessionId
StartedAtUtc
EndedAtUtc
DurationSeconds
FilePath
FileName
FileSizeBytes
Status
CreatedAtUtc

أضف Enums مناسبة بدل Magic Strings عندما يكون ذلك منطقيًا.

أنشئ Interfaces مناسبة مثل:

IActivityTracker
IActivityRepository
IActivityContextProvider
IScreenRecordingService
IRecordingRepository
IRecordingLocatorService
IActivityReportExporter
IStorageManagementService

يمكنك تحسين الأسماء أو تقسيم Interfaces إذا كان التصميم الأفضل يتطلب ذلك.

==================================================
Infrastructure
==================================================

استخدم EF Core + SQLite.

قاعدة البيانات:

%ProgramData%\ActivityMonitor\Data\activity.db

استخدم Environment.SpecialFolder.CommonApplicationData بدل Hardcoded paths.

أنشئ DbContext.

أنشئ Configurations وIndexes المناسبة.

اهتم خصوصًا بالبحث حسب:

StartedAtUtc
EndedAtUtc
MachineId
SessionId
ProcessName

استخدم UTC في قاعدة البيانات.

نفذ Initialization/Migrations بطريقة مناسبة لتطبيق Desktop/Service.

لا تجعل خطأ مؤقت في SQLite يسقط Agent بالكامل.

==================================================
Activity Tracking
==================================================

الـAgent يعمل داخل Interactive User Session.

راقب Foreground Window.

استخدم Windows APIs المناسبة مثل:

GetForegroundWindow
GetWindowThreadProcessId

واستخدم WinEventHook أو event-driven approach عندما يكون مناسبًا بدل polling السريع.

سجل:

Process Name
Process ID
Executable Path
Window Title
Start Time
End Time
Duration
Machine
Session
User identifier

عند استمرار نفس السياق لا تنشئ آلاف الصفوف.

ادمج مدة النشاط في Event منطقي واحد.

تعامل مع:

AccessDenied
Process exited
Invalid window handles
Race conditions

بدون انهيار Agent.

==================================================
Idle Detection
==================================================

استخدم Windows API مناسبًا مثل GetLastInputInfo.

اجعل IdleThreshold configurable.

Default:

5 minutes

أنشئ Activity/Event مناسبًا عند دخول المستخدم في Idle وعند العودة.

==================================================
Windows Session Events
==================================================

تعامل قدر الإمكان مع:

Logon
Logoff
Lock
Unlock
Sleep
Resume
Shutdown

سجلها كأحداث مناسبة.

==================================================
Context Providers
==================================================

صمم النظام بحيث نستطيع معرفة سياق إضافي للتطبيق.

استخدم:

IActivityContextProvider

وأنشئ architecture قابلة لإضافة:

ExplorerContextProvider
VisualStudioContextProvider
VSCodeContextProvider
BrowserContextProvider

في النسخة الحالية طبق فقط ما يمكن الحصول عليه بطريقة مستقرة ومشروعة من Windows.

لا تستخدم Accessibility أو UI Automation بشكل عدواني لمسح محتويات التطبيقات.

لا تسجل:

Keystrokes
Passwords
Clipboard contents
Form input
Camera
Microphone

==================================================
Windows Service
==================================================

ActivityMonitor.Service يجب أن يكون Windows Service حقيقيًا.

استخدم UseWindowsService أو الطريقة الحديثة المناسبة في .NET 10.

الخدمة مسؤولة عن:

- Background maintenance.
- Storage management.
- Retention.
- Health information.
- Reports scheduling إذا تم تفعيله.
- Logs.
- المهام التي لا تحتاج Interactive Desktop.

مهم جدًا:

Windows Service يعمل في Session 0.

لا تحاول تسجيل الشاشة من Session 0.

ولا تستخدم تقنيات غير آمنة لإجبار Service على التفاعل مع Desktop.

==================================================
Agent Startup
==================================================

ActivityMonitor.Agent يجب أن يعمل داخل جلسة المستخدم.

اجعله يبدأ تلقائيًا عند تسجيل الدخول باستخدام طريقة Windows مناسبة وآمنة.

اختر بين:

Scheduled Task
Startup registration
أو طريقة Windows رسمية أفضل.

نفذ الطريقة الأنسب.

لا تعتمد على أن يقوم المستخدم بتشغيل Agent يدويًا كل مرة.

==================================================
Screen Recording
==================================================

نفذ Screen Recording داخل ActivityMonitor.Agent.

استخدم تقنية حديثة مناسبة لـWindows.

افحص أفضل اختيار بين:

Windows Graphics Capture
Desktop Duplication API
Media Foundation
Hardware accelerated encoding

اختر Architecture مستقرة ومتوافقة مع .NET 10.

تجنب FFmpeg executable خارجي إذا كان Windows Media Foundation أو Windows APIs تستطيع تنفيذ المطلوب بصورة مستقرة.

لكن إذا كان استخدام مكتبة موثوقة يوفر حلًا أكثر استقرارًا، يمكنك استخدامها بشرط فحص الترخيص وعدم إدخال dependency غير ضرورية.

التسجيل:

MP4

Default segment:

10 minutes

Configurable:

RecordingSegmentMinutes

كل 10 دقائق أغلق الملف الحالي بصورة صحيحة وابدأ ملفًا جديدًا.

لا تنشئ فيديو واحدًا لساعات.

==================================================
Video Storage
==================================================

المسار الأساسي:

%ProgramData%\ActivityMonitor\Recordings\

استخدم:

Recordings
    MachineId
        YYYY
            MM
                DD
                    HH
                        YYYY-MM-DD_HH-mm-ss.mp4

مثال:

%ProgramData%\ActivityMonitor\Recordings\PC-001\2026\10\04\14\2026-10-04_14-00-00.mp4

ثم:

2026-10-04_14-10-00.mp4
2026-10-04_14-20-00.mp4

استخدم أسماء ملفات لا تتعارض عند إعادة تشغيل Agent.

سجل كل فيديو في RecordingSegment.

سجل:

Start
End
Duration
FileName
FullPath
Size
Status

إذا حدث Crash أثناء التسجيل، حاول ألا يؤثر ذلك على التسجيلات السابقة.

==================================================
Activity ↔ Video Linking
==================================================

هذه من أهم وظائف النظام.

يجب أن أستطيع أخذ ActivityEvent ومعرفة:

أي فيديو يحتويه؟
وأين داخل الفيديو؟

مثال:

Recording:

14:20:00 → 14:30:00

Activity:

14:23:10 → 14:27:42

النتيجة:

RecordingFile:
2026-10-04_14-20-00.mp4

Offset:
00:03:10

أنشئ:

IRecordingLocatorService

أو تصميمًا أفضل يؤدي نفس الوظيفة.

يجب أن يستطيع البحث باستخدام:

Timestamp

أو:

ActivityEvent

ويعيد:

RecordingSegment
FilePath
Offset

إذا امتد ActivityEvent عبر مقطعين فيديو، تعامل مع ذلك بصورة صحيحة ولا تفترض دائمًا وجود فيديو واحد فقط.

==================================================
Excel Reports
==================================================

ActivityMonitor.Reporting مسؤول عن التقارير.

SQLite هي Source of Truth.

Excel ليس Database.

اختر مكتبة Excel مناسبة ذات ترخيص يسمح باستخدام المشروع بالشكل المطلوب.

لا تستخدم مكتبة ذات قيود تجارية غير واضحة دون ذكر ذلك.

مكان التقارير:

%ProgramData%\ActivityMonitor\Reports\

الترتيب:

Reports
    MachineId
        YYYY
            MM
                2026-10-04.xlsx

أنشئ Daily Excel Report.

يحتوي على:

Summary
Timeline
Applications
Recordings

-------------------------
Summary
-------------------------

Date
Machine
User
Total Active Time
Total Idle Time
First Activity
Last Activity
Applications Used
Recording Count
Recording Size

-------------------------
Timeline
-------------------------

Start
End
Duration
Application
Window Title
Process Path
Context Path
Event Type
Recording File
Recording Offset

مثال:

14:23:10
14:27:42
00:04:32
Visual Studio
RetailSystem
...
Active
2026-10-04_14-20-00.mp4
00:03:10

أضف Hyperlink إلى الفيديو المحلي عندما يكون ذلك مناسبًا.

الضغط على رابط الفيديو من Excel يجب أن يفتح ملف الفيديو إن كان موجودًا.

ضع أيضًا Recording Offset حتى يعرف المستخدم أين يبدأ النشاط داخل الفيديو.

-------------------------
Applications
-------------------------

Application
Total Duration
Usage Count
Percentage

-------------------------
Recordings
-------------------------

Start
End
Duration
File
Full Path
Size
Status

نسق Excel بصورة مقروءة:

Headers
Auto width
Date formatting
Duration formatting
Freeze header
Filters

==================================================
CSV / TXT Export
==================================================

أضف Export إلى CSV.

يحتوي مثلًا:

Timestamp
EndTime
Application
WindowTitle
Context
Duration
EventType
RecordingFile
RecordingOffset

إذا أضفت TXT فاجعله صيغة بشرية بسيطة للمراجعة.

لكن:

SQLite = Source of Truth.

Excel/CSV/TXT = Export فقط.

==================================================
Technical Logs
==================================================

افصل Logs التقنية عن Activity Data.

المسار:

%ProgramData%\ActivityMonitor\Logs\

مثل:

agent-2026-10-04.log
service-2026-10-04.log

Logs تشمل:

Agent Started
Agent Stopped
Database Initialized
Recording Started
Recording Closed
Recording Failure
Database Failure
Access Denied
Retention Cleanup
Unexpected Exception

لا تضع النشاط اليومي للمستخدم داخل Technical Log.

==================================================
Storage Management
==================================================

أنشئ:

IStorageManagementService

يحسب:

Database size
Recording size
Reports size
Logs size
Total size
Available disk space

Configuration:

RecordingEnabled
RecordingSegmentMinutes
RecordingRetentionDays
ActivityRetentionDays
ReportRetentionDays
LogRetentionDays
MaxStorageSizeGB
MinimumFreeDiskSpaceGB

قبل بدء Recording تحقق من مساحة القرص.

إذا أصبحت المساحة منخفضة:

1. أغلق التسجيل الحالي بأمان عند الحاجة.
2. نفذ Retention Policy.
3. احذف أقدم تسجيلات مؤهلة للحذف.
4. سجل ما تم حذفه.
5. لا تحذف ملفات عشوائية خارج مجلد ActivityMonitor.

لا تحذف ActivityEvents لمجرد حذف الفيديو.

إذا حذف الفيديو:

حدث RecordingSegment.Status بشكل مناسب.

==================================================
Configuration
==================================================

استخدم Options Pattern.

أنشئ إعدادات واضحة مثل:

ActivityTracking:
Enabled
IdleThresholdMinutes

Recording:
Enabled
SegmentMinutes
RetentionDays
MaxStorageSizeGB
MinimumFreeDiskSpaceGB

Reports:
ExcelEnabled
CsvEnabled
DailyReportEnabled

Storage:
ActivityRetentionDays
LogRetentionDays
ReportRetentionDays

Logging:
appropriate configuration

ضع Defaults منطقية.

تحقق من Configuration عند Startup.

==================================================
Performance
==================================================

هذه أداة تعمل طوال اليوم ولذلك الأداء مهم جدًا.

لا تستخدم Busy Loop.

لا تستخدم polling كل milliseconds.

لا تكتب SQLite باستمرار بدون داعٍ.

استخدم batching/debouncing عندما يكون مناسبًا.

استخدم async I/O.

Report generation يجب ألا يوقف Activity Tracking.

Retention يجب ألا يوقف Recording.

راقب Memory leaks وHandle leaks.

لا تحتفظ بـProcess objects أوWindow handles أكثر من اللازم.

==================================================
Reliability
==================================================

يجب أن يتحمل البرنامج:

Restart
Shutdown
Sleep
Resume
Agent crash
Service restart
SQLite temporary lock
Access denied
Process termination
Screen recording failure
Encoder failure
Disk full

بدون فقد كل البيانات السابقة.

أضف Global exception handling المناسب ولكن لا تخفِ الأخطاء.

سجل الأخطاء بطريقة مفيدة.

==================================================
Privacy / Transparency
==================================================

النظام مخصص لأجهزة مؤسسة توجد عليها سياسة معلنة للمراقبة.

يجب أن تكون حالة:

Activity Monitoring
Screen Recording

قابلة للرؤية للمستخدم المخول.

لا تنفذ:

Process hiding
Service hiding
Task Manager hiding
Anti-detection
EDR bypass
Antivirus bypass
Security software disabling
Keylogging
Password capture
Clipboard capture
Camera capture
Microphone capture
Browser password extraction
Credential extraction

ولا تحاول تجاوز صلاحيات Windows.

==================================================
Tests
==================================================

استخدم ActivityMonitor.Tests.

أضف اختبارات لـ:

Duration calculation
Activity merging
Idle state transitions
Recording locator
Recording offset
Activity spanning multiple recordings
Path generation
Filename generation
Retention policy
Storage limits
Report generation
Configuration validation

شغل الاختبارات وأصلح المشاكل.

==================================================
Installation
==================================================

بعد نجاح البرنامج أنشئ طريقة Deployment مناسبة لـWindows.

أريد في النهاية طريقة تثبيت بسيطة تقوم بـ:

- تثبيت الملفات.
- إنشاء المجلدات المطلوبة.
- تثبيت Windows Service.
- ضبط Service على Automatic.
- ضبط Agent ليبدأ مع User Logon.
- إنشاء Configuration.
- إعداد الصلاحيات المطلوبة لمجلد ProgramData.
- توفير Uninstall صحيح.

لا تجعل Uninstall يحذف التسجيلات وقاعدة البيانات تلقائيًا دون قرار واضح.

إذا احتجنا Installer project أو WiX أو MSIX أو طريقة أفضل، اختر الأنسب واشرح السبب.

==================================================
Development Mode
==================================================

اجعل من السهل تشغيل النظام أثناء التطوير بدون تثبيت Service كل مرة.

أريد أن أستطيع تشغيل Agent من Visual Studio وتصحيح الأخطاء Debug.

وفّر Development configuration مناسبة.

==================================================
README
==================================================

أنشئ README.md شاملًا يحتوي على:

Architecture
Projects
Dependencies
How to build
How to run Agent
How to debug
How to install Service
How to install full application
Configuration
Database location
Recordings location
Reports location
Logs location
How Activity ↔ Recording linking works
Known limitations

==================================================
طريقة التنفيذ المطلوبة منك
==================================================

لا تتوقف عند كتابة خطة فقط.

ابدأ بفحص Solution الحالي.

ثم:

1. أصلح Project References.
2. اضبط csproj files.
3. أضف NuGet packages.
4. أنشئ Architecture.
5. نفذ Core.
6. نفذ SQLite Infrastructure.
7. Build.
8. أصلح الأخطاء.
9. نفذ Activity Tracking.
10. نفذ Idle/Session tracking.
11. Build + Tests.
12. نفذ Windows Service.
13. نفذ Agent startup.
14. نفذ Screen Recording.
15. اختبر MP4 segmentation.
16. نفذ Activity ↔ Recording linking.
17. نفذ Excel.
18. نفذ CSV/TXT.
19. نفذ Storage Management.
20. نفذ Retention.
21. أكمل Tests.
22. Build كامل Release.
23. أنشئ Deployment/Installer المناسب.
24. أنشئ README.

لا تطلب مني تنفيذ خطوات يدوية أثناء ذلك إلا إذا واجهت خطوة تتطلب مني فعلًا قرارًا أو صلاحية لا تستطيع تنفيذها.

إذا كان هناك قرار تقني بسيط، اختر الحل الأفضل بنفسك واستمر.

إذا وجدت أن أحد المتطلبات غير ممكن أو غير موثوق على Windows، لا تستخدم Hack غير آمن لتحقيقه. نفذ أفضل بديل رسمي واشرح القيد.

==================================================
معايير الانتهاء
==================================================

لا تعتبر المهمة منتهية حتى:

- Solution builds successfully.
- Tests pass.
- Agent يعمل.
- SQLite تعمل.
- Activity tracking يعمل.
- Idle detection يعمل.
- Session events تعمل بالقدر الممكن.
- Screen recording ينتج MP4 صالحًا.
- Segmentation يعمل.
- Recording metadata محفوظة.
- Activity ↔ Video lookup يعمل.
- Excel export يعمل.
- CSV export يعمل.
- Storage policies تعمل.
- Logs تعمل.
- Release build ينجح.
- توجد طريقة تثبيت واضحة.
- README موجود.

في النهاية أعطني تقريرًا مختصرًا يحتوي:

Implemented
Architecture
NuGet packages used
Windows APIs used
Database schema
Recording technology
Paths used
Installer method
Tests results
Build results
Known limitations
وأي شيء يحتاج تحسينًا في الإصدار التالي.