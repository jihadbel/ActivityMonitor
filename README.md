# ActivityMonitor

## تحديث الاعتمادية والصلاحيات — 15 أكتوبر 2026

- **بيانات المستخدم الجديدة:** `%LOCALAPPDATA%\ActivityMonitor`، بقاعدة وتسجيلات وتقارير وإعدادات خاصة بكل حساب Windows. مالك الحساب والمسؤول وSYSTEM فقط يملكون حق الوصول إلى المخزن. هذا ليس سجلًا غير قابل للعبث من مالكه.
- **الأرشيف السابق:** يبقى في `%ProgramData%\ActivityMonitor`. يقيّد المثبت المحدث صلاحياته للمسؤول وSYSTEM؛ لم تُطبّق هذه الصلاحيات على أي تثبيت قائم بمجرد تعديل المصدر. لعرضه شغّل Viewer كمسؤول مع `--legacy-archive`.
- **الصيانة:** Agent يشغّل عامل صيانة خلفيًا لمخزن المستخدم، حتى عند تعطيل سجل التطبيقات. Windows Service يصون الأرشيف القديم؛ توقفه لا يعني فشل مخزن المستخدم.
- **الإعدادات:** افتراضات المسؤول في `%ProgramData%\ActivityMonitor\appsettings.json`، وتجاوزات المستخدم في `%LOCALAPPDATA%\ActivityMonitor\appsettings.json`. لا يطلب حفظ الإعدادات المحلية رفع الصلاحيات؛ تعديل بدء خدمة Windows يتطلبه عند تغيير القيمة فقط.
- **الاحتفاظ:** مدد مستقلة للفيديو والنشاط والتقارير والسجل. حصة المساحة قد تختصر الاحتفاظ الفعلي بالفيديو.
- **الربط والاسترداد:** مطابقة الجهاز والجلسة والمستخدم، heartbeat وملكية للعملية، وفحص فيديو Media Foundation؛ الحالة `Recovered` تعني استرداد ملف تم التحقق من تدفق الفيديو ومدته.
- **بدء التشغيل:** تسجيل Run عادي دون Highest، مع `--autostart` الذي يحترم تفضيل كل مستخدم. المثبت يزيل المهمة القديمة، ولا يشغّل Agent فورًا افتراضيًا. يمكن تشغيله من العارض أو عند الدخول التالي.
- **الترقية:** `user_version=2` مع نسخة SQLite متسقة قبل تغيير المخطط القديم. الحزم قابلة للتحقق عبر SHA-256؛ هذه ليست توقيعًا من ناشر موثوق.

لعمل نسخة متسقة تشمل WAL دون تشغيل التقاط الشاشة:

```powershell
.\ActivityMonitor.Agent.exe --backup-db "D:\Backups\activity-2026-10-15.db"
```

الوجهة يجب أن تكون ملفًا جديدًا. اختبر الاستعادة في مكان منفصل. للاستعادة إلى المخزن الأصلي: أوقف جميع Agents للحساب، احتفظ بنسخة من مجلد Data الحالي بما فيه WAL/SHM، ثم استبدل مجلد Data كاملًا بملف النسخة باسم `activity.db` دون ملفات WAL/SHM قديمة، وأعد التشغيل. لا تستبدل ملف قاعدة مفتوحة.

بناء وفحص قابلان لإعادة الإنتاج:

```powershell
dotnet restore ActivityMonitor.slnx --locked-mode
dotnet build ActivityMonitor.slnx -c Release --no-restore
dotnet test ActivityMonitor.slnx -c Release --no-build
```

نشر حزمة Windows x64 من المصدر:

```powershell
.\deploy\Publish.ps1
```

الافتراضي SelfContained=true. تم اختبار مسار النشر أيضًا مع SelfContained=false، والذي يحتاج .NET 10 وWindows Desktop Runtime 10 على الجهاز.

لمراجعة مخزن مستخدم آخر، يستطيع المسؤول تشغيل Viewer مع `--user-store "C:\Users\OtherUser\AppData\Local\ActivityMonitor"`. وضع مراجعة الأرشيف يعطّل التحكم في Agent والإعدادات. الصيانة الخاصة بالمستخدم تعمل أثناء تشغيل Agent؛ بيانات الحساب الذي لا يشغّل Agent تُصان عند تشغيله التالي، وليس بواسطة خدمة الأرشيف القديم.

التفاصيل: [سجل الإصلاحات والتحقق](docs/FIXES_2026-10-15.md). التحليل السابق محفوظ كمرجع تاريخي ولا يصف حالة كل مشكلة بعد الإصلاح.


نظام متكامل لمراقبة النشاط وتسجيل الشاشة لنظام التشغيل Windows مبني باستخدام **C#** و **.NET 10** بنمط Modular Clean Architecture.

يعمل النظام محليًا على جهاز المستخدم باستخدام مكتبات .NET وWindows، دون خدمة سحابية لتخزين النشاط.
توجد اختصارات واضحة للعارض ومؤشرات لحالة Agent والتسجيل. الأداء طويل المدة يحتاج قياسًا على الأجهزة المستهدفة.

---

## 🏗️ Architecture & Projects Structure

يتكون المشروع من ستة مشاريع متناسقة ومنفصلة المسؤوليات:

```
ActivityMonitor/
├── ActivityMonitor.Core/             # العقود المشتركة، النماذج، الكيانات والواجهات
├── ActivityMonitor.Infrastructure/   # طبقة البيانات (EF Core + SQLite) وتكامل Windows APIs
├── ActivityMonitor.Agent/            # برنامج العميل التفاعلي (User Session) لمراقبة النوافذ والتسجيل
├── ActivityMonitor.Reporting/        # تصدير تقارير Excel (ClosedXML) و CSV و TXT
├── ActivityMonitor.Service/          # خدمة Windows Service الخلفية للصيانة والأرشفة وجدولة التقارير
├── ActivityMonitor.Tests/            # اختبارات الوحدة والتكامل (xUnit)
└── deploy/                           # سكربتات النشر والتثبيت وإلغاء التثبيت
```

### 1. ActivityMonitor.Core
- **الكيانات الأساسية**:
  - `ActivityEvent`: تسجيل تفاصيل كل نافذة/عملية نشطة، العنوان، المسار التنفيذي، سياق الملفات، نوع الحدث، والربط الزمني بالفيديو.
  - `RecordingSegment`: تسجيل المقاطع الزمنية للفيديو (MP4)، أوقات البداية والنهاية، والمسار، والحجم، وحالة المقطع.
- **Enums**:
  - `ActivityEventType`: `ActiveWindow`, `IdleStart`, `IdleEnd`, `SessionLock`, `SessionUnlock`, `SessionLogon`, `SessionLogoff`, `SystemSleep`, `SystemResume`, `SystemShutdown`.
  - `RecordingStatus`: `InProgress`, `Completed`, `Corrupted`, `Deleted`, `DiskSpaceReclaimed`.
- **Contracts & Interfaces**:
  - `IActivityTracker`, `IActivityRepository`, `IScreenRecordingService`, `IRecordingRepository`, `IRecordingLocatorService`, `IActivityReportExporter`, `IStorageManagementService`, `IActivityContextProvider`.

### 2. ActivityMonitor.Infrastructure
- **قاعدة البيانات**: SQLite عبر Entity Framework Core مع تفعيل نمط **WAL Mode** (`PRAGMA journal_mode=WAL;`) لأقصى سرعة تزامن واستقرار.
- **الفهارس المتقدمة**: على حقول التواريخ (`StartedAtUtc`, `EndedAtUtc`) واسم الجهاز والعملية والحالة.
- **استخراج مسارات Windows Explorer**: مزود `WindowsExplorerContextProvider` يعتمد على `Shell.Application` COM الرسمي لاستخراج المسار الفعلي للمجلد أو الملف المفتوح بدون تقنيات فحص عدوانية.
- **السجل التقني**: `ActivityFileLoggerProvider` يسجل الأحداث التقنية بشكل منفصل تمامًا عن بيانات النشاط، مع تدوير يومي.

### 3. ActivityMonitor.Agent
- **يعمل في جلسة المستخدم التفاعلية (Interactive Session)**.
- **مراقبة النوافذ النشطة**: يجمع بين `SetWinEventHook` للاستجابة اللحظية الفورية عند تغيير النافذة، وفحص خفيف مجدول (Polling) لتحديث العناوين وحالة الخمول.
- **دمج الأحداث الذكي (Activity Merging)**: عند استمرار العمل على نفس التطبيق والسياق لا يتم إغراق قاعدة البيانات بآلاف السجلات، بل يتم دمج المدة الزمنية في حدث منطقي واحد.
- **اكتشاف الخمول (Idle Detection)**: باستخدام `GetLastInputInfo` (افتراضيًا 5 دقائق قابلة للتعديل).
- **تسجيل مرتبط بالنشاط**: يبدأ الفيديو عند وجود إدخال من الماوس أو لوحة المفاتيح، ويتوقف بعد مهلة الخمول المحددة. أثناء توقف الفيديو لا تُنشأ أحداث تطبيقات جديدة، ويُستأنف الاثنان معًا عند عودة المستخدم.
- **أحداث جلسة Windows**: التقاط القفل وإلغاء القفل وتسجيل الدخول والخروج والـ Sleep / Resume وShutdown عبر `SystemEvents`.
- **تسجيل الشاشة**: باستخدام `ScreenRecorderLib` عبر Windows Media Foundation وDesktop Duplication مع تسريع العتاد (Hardware Acceleration) وتنسيق MP4 بدون الحاجة لـ FFmpeg خارجي.
- **تقسيم الفيديو (Segmentation)**: تقطيع دوري آمن (كل 10 دقائق افتراضيًا) لتجنب إنتاج ملفات ضخمة وحماية التسجيلات من الانهيار.

### 4. ActivityMonitor.Reporting
- **تقارير Excel غنية بواسطة ClosedXML (ترخيص MIT)**:
  - صفحة **Summary**: ملخص تنفيذي للمؤشرات والنشاط الإجمالي وحجم التسجيلات.
  - صفحة **Timeline**: جدول تفصيلي بالأحداث، المدة، السياق، ورابط Hyperlink مباشر لتشغيل ملف الفيديو المحلي في مشغل النظام.
  - صفحة **Applications**: ترتيب التطبيقات حسب زمن الاستخدام والنسبة المئوية.
  - صفحة **Recordings**: قائمة مقاطع الفيديو وأحجامها ومدتها وحالتها.
- **تصدير CSV و TXT**: تقارير جدولية وتلخيص نصي بشري للمراجعة السريعة.

### 5. ActivityMonitor.Service
- **خدمة Windows حقيقية تعمل في Session 0**:
  - تم تصميمها بأمان تام (لا تحاول التفاعل مع الشاشة أو التسجيل من Session 0).
  - تتولى مهام الصيانة الدورية وتطبيق سياسة الأرشفة (Retention Policy).
  - فحص سعة القرص وحذف المقاطع الأقدم عند امتلاء المساحة المحددة وتحديث حالة المقاطع دون المساس بسجلات الأحداث `ActivityEvents`.
  - الجدولة التلقائية لإصدار تقارير اليوم المكتمل السابق بصيغ Excel وCSV وTXT (افتراضيًا عند 00:05)، مع استكمال آخر 7 أيام مفقودة.

### 6. ActivityMonitor.Viewer (Local Player & Viewer GUI)
- **واجهة مراجعة وتصفح محلية حديثة وخفيفة (WPF / Arabic RTL Light Theme)**:
  - **تصفح الجدول الزمني لليوم**: عرض الأحداث مع المدة، التطبيق، العنوان، وسياق العمل.
  - **مشغل فيديو متزامن داخل نفس النافذة**: مشغل مدمج يسمح بالقفز التلقائي اللحظي (1-Click Jump) إلى الـ Offset الزمني للفيديو بمجرد النقر على زر "▶ Play" أو صف النشاط.
  - **التحكم بالخدمة والـ Agent**: مؤشر حالة فوري وزر لتشغيل وإيقاف خدمة Windows Service (`ActivityMonitorService`) وتشغيل/إيقاف العميل (`ActivityMonitor.Agent`).
  - **مؤشرات أداء تنفيذية (KPI Cards)**: ملخص فوري لزمن العمل النشط، زمن الخمول، التطبيق الأكثر استخدامًا، وعدد مقاطع الفيديو المسجلة.
  - **البحث والتصفية الفورية**: تصفية الأنشطة حسب اسم البرنامج أو العنوان أو المسار بنص البحث اللحظي.


---

## 📁 Locations & Paths

يستخدم Agent وViewer مخزنًا خاصًا بكل مستخدم داخل `%LOCALAPPDATA%\ActivityMonitor`.
خدمة Windows تصون الأرشيف المشترك القديم داخل `%ProgramData%\ActivityMonitor` بصلاحيات المسؤول.
لا يُنقل الأرشيف المختلط إلى حساب مستخدم تلقائيًا.

| المكون | المسار الافتراضي |
| :--- | :--- |
| **قاعدة البيانات** | `%LOCALAPPDATA%\ActivityMonitor\Data\activity.db` |
| **مقاطع الفيديو** | `%LOCALAPPDATA%\ActivityMonitor\Recordings\{MachineId}\{YYYY}\{MM}\{DD}\{HH}\{YYYY-MM-DD_HH-mm-ss}.mp4` |
| **التقارير اليومية** | `%LOCALAPPDATA%\ActivityMonitor\Reports\{MachineId}\{YYYY}\{MM}\{YYYY-MM-DD}.xlsx` |
| **السجلات التقنية** | `%LOCALAPPDATA%\ActivityMonitor\Logs\agent-{YYYY-MM-dd}.log` و `service-{YYYY-MM-dd}.log` |

---

## 🔗 How Activity ↔ Recording Linking Works

تعتبر وظيفة الربط بين النشاط ومقطع الفيديو الزمني إحدى الركائز الأساسية للنظام:
1. يملك كل `RecordingSegment` تاريخ بداية وتاريخ نهاية دقيق (`StartedAtUtc`, `EndedAtUtc`).
2. يقوم `IRecordingLocatorService` باستقبال أي `ActivityEvent` والبحث عن جميع مقاطع الفيديو التي تغطي الفترة الزمنية للحدث.
3. يحسب النظام الـ **Offset** الدقيق داخل الفيديو:
   $$\text{Offset} = \max(\text{ActivityStart}, \text{SegmentStart}) - \text{SegmentStart}$$
4. **التعامل مع الأحداث الممتدة عبر مقطعين (Spanning)**:
   إذا امتد النشاط عبر مقطعي فيديو متتاليين (مثل حدث بدأ 14:28 وانتهى 14:33 بينما تم قطع الفيديو عند 14:30)، يقوم النظام بإرجاع نتيجتين دقيقتين: المقطع الأول بالإزاحة والمدة داخله، والمقطع الثاني من الإزاحة 00:00:00 بالمدة المتبقية.
5. في تقرير Excel، يوفر التقرير رابطًا قابلاً للنقر (`file:///...`) يفتح مقطع الفيديو مباشرة مع تحديد توقيت الـ Offset لبدء المشاهدة من لحظة الحدث المحددة.

---

## ⚙️ Configuration (`appsettings.json`)

يتم التحكم في كافة الخيارات عبر نمط Options Pattern:

```json
{
  "ActivityTracking": {
    "Enabled": true,
    "PollingIntervalMs": 2000,
    "IdleThresholdMinutes": 5,
    "WindowDebounceMs": 300
  },
  "Recording": {
    "Enabled": true,
    "RecordOnlyWhenUserActive": true,
    "SegmentMinutes": 10,
    "FrameRate": 15,
    "VideoBitrateKbps": 1200,
    "VideoQuality": 60,
    "RetentionDays": 14,
    "MaxStorageSizeGB": 50,
    "MinimumFreeDiskSpaceGB": 2
  },
  "Reports": {
    "ExcelEnabled": true,
    "CsvEnabled": true,
    "TxtEnabled": true,
    "DailyReportEnabled": true,
    "DailyReportScheduleTime": "00:05:00",
    "BackfillDays": 7
  },
  "Storage": {
    "RecordingRetentionDays": 14,
    "ActivityRetentionDays": 90,
    "ReportRetentionDays": 90,
    "LogRetentionDays": 30,
    "MaxStorageSizeGB": 50,
    "MinimumFreeDiskSpaceGB": 2
  }
}
```

---

## 🚀 Building & Testing

### متطلبات التشغيل والتطوير:
- Windows 10 / Windows 11 / Windows Server 2022+ (x64)
- .NET 10.0 SDK

### أوامر البناء:
```powershell
# بناء الحل بالكامل (Debug)
dotnet build ActivityMonitor.slnx

# بناء الحل في وضع الإنتاج (Release)
dotnet build ActivityMonitor.slnx -c Release

# تشغيل الاختبارات الآلية
dotnet test ActivityMonitor.Tests/ActivityMonitor.Tests.csproj
```

---

## 🛠️ Development & Debugging Mode

يمكنك تشغيل وتصحيح أخطاء الـ Agent والـ Service مباشرة من Visual Studio أو سطر الأوامر دون الحاجة لتثبيت الـ Windows Service:

1. **تشغيل الـ Agent في وضع التطوير**:
   - افتح المشروع في Visual Studio واضبط `ActivityMonitor.Agent` كمشروع البداية (Startup Project).
   - عند تشغيله مباشرة يظهر Console يحتوي على سجل الأحداث اللحظية.
   - يتوفر ملف `appsettings.Development.json` بإعدادات سريعة للتجربة (مقاطع فيديو مدتها دقيقة واحدة، واكتشاف خمول بعد دقيقة).

2. **تشغيل الـ Agent في الوضع الصامت (Silent Mode)**:
   ```powershell
   dotnet run --project ActivityMonitor.Agent -- --silent
   ```
   يقوم هذا الخيار بإخفاء نافذة الـ Console فورًا ليعمل البرنامج في الخلفية التامة.

---

## 📦 Deployment & Installation

يتوفر في مجلد `deploy/` سكربتات PowerShell مؤتمتة لإعداد النظام:

### 1. تجهيز حزم النشر (Publish):
```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Publish.ps1
```
يقوم هذا السكربت بتجميع مشروعي `ActivityMonitor.Service` و `ActivityMonitor.Agent` في وضع Release ووضعهما داخل `deploy\dist\`.

### 2. تثبيت البرنامج والخدمة (Install):
قم بفتح PowerShell كمسؤول (Run as Administrator) وشغّل:
```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\Install.ps1
```
يقوم السكربت بالمهام التالية تلقائيًا:
- إنشاء المجلدات في `C:\Program Files\ActivityMonitor` ومجلدات `%LOCALAPPDATA%\ActivityMonitor`.
- ضبط صلاحيات الأمان (NTFS ACLs) لتمكين كل من خدمة النظام والمستخدم من الوصول لقاعدة البيانات والتسجيلات.
- تثبيت Windows Service باسم `ActivityMonitor.Service` وضبطها للعمل تلقائيًا عند تشغيل الجهاز (`Automatic`).
- إنشاء Scheduled Task لتشغيل `ActivityMonitor.Agent.exe` في الوضع الصامت (`--silent`) عند تسجيل دخول أي مستخدم.
- تشغيل الخدمة والـ Agent فورًا.

### 3. تشغيل واجهة المراجعة والمشغل المحلي (Viewer GUI):
لتصفح التقارير وتشغيل الفيديوهات مع القفز الزمني اللحظي والتحكم بالخدمة:
```powershell
# من مسار الحزم الجاهزة:
.\deploy\dist\Viewer\ActivityMonitor.Viewer.exe

# أو أثناء التطوير:
dotnet run --project .\ActivityMonitor.Viewer\ActivityMonitor.Viewer.csproj -c Release
```

### 4. إلغاء التثبيت الآمن (Uninstall):
قم بفتح PowerShell كمسؤول وشغّل:
```powershell
# إزالة البرنامج مع الحفاظ على البيانات والتسجيلات بأمان:
powershell -ExecutionPolicy Bypass -File .\deploy\Uninstall.ps1

# أو إزالة البرنامج ومسح كافة البيانات والتسجيلات بالكامل:
powershell -ExecutionPolicy Bypass -File .\deploy\Uninstall.ps1 -PurgeData
```

---

## ⚠️ Known Limitations & Considerations

1. **Session 0 Separation**:
   خدمة Windows Service تعمل داخل Session 0 الآمنة ولا يمكنها تسجيل شاشة المستخدم أو قراءة النوافذ التفاعلية؛ لذا تم إسناد التسجيل ومراقبة النوافذ بالكامل لمشروع الـ Agent الذي يعمل داخل جلسة المستخدم، بينما تقتصر الخدمة على مهام الصيانة والأرشفة وجدولة التقارير.
2. **الخصوصية والشفافية**:
   لا يسجل النظام ضغطات المفاتيح أو كلمات المرور أو الحافظة أو الكاميرا. إعدادات Agent في هذه الحزمة تفعّل الميكروفون فقط، ويمكن تعطيله من الإعدادات. صوت النظام معطّل. يتطلب تسجيل الصوت علم المستخدم وموافقته، وتبقى مؤشرات استخدام الميكروفون الخاصة بـWindows ظاهرة. يعمل Agent في الخلفية دون نوافذ أو تنبيهات إضافية، ولا تفتح واجهة الإعدادات جلسة الميكروفون ما لم يُفعّل الخيار. إعدادات المستخدم المحفوظة تتقدم على إعدادات الحزمة، والخيارات البرمجية الاحتياطية تبقي الصوت معطّلًا عند غياب الإعدادات.
3. **صلاحيات النوافذ المرتفعة (Elevated Windows)**:
   تم استخدام `QueryFullProcessImageName` للتعامل مع البرامج التي تعمل بصلاحيات Administrator حتى يتم استخراج مساراتها التنفيذية دون التعرض لخطأ `Access Denied`.
