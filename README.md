# ActivityMonitor

نظام متكامل لمراقبة النشاط وتسجيل الشاشة لنظام التشغيل Windows مبني باستخدام **C#** و **.NET 10** بنمط Modular Clean Architecture.

يعمل النظام في صمت تام وبأداء فائق واستهلاك منخفض للموارد ومحليًا بالكامل داخل جهاز المستخدم دون الاعتماد على أي خدمات سحابية أو طرف ثالث خارجي.

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
  - الجدولة التلقائية لإصدار التقارير اليومية بصيغ Excel و CSV و TXT (افتراضيًا عند الساعة 23:55).

### 6. ActivityMonitor.Viewer (Local Player & Viewer GUI)
- **واجهة مراجعة وتصفح محلية حديثة وخفيفة (WPF / Fluent Dark Theme)**:
  - **تصفح الجدول الزمني لليوم**: عرض الأحداث مع المدة، التطبيق، العنوان، وسياق العمل.
  - **مشغل فيديو متزامن داخل نفس النافذة**: مشغل مدمج يسمح بالقفز التلقائي اللحظي (1-Click Jump) إلى الـ Offset الزمني للفيديو بمجرد النقر على زر "▶ Play" أو صف النشاط.
  - **التحكم بالخدمة والـ Agent**: مؤشر حالة فوري وزر لتشغيل وإيقاف خدمة Windows Service (`ActivityMonitorService`) وتشغيل/إيقاف العميل (`ActivityMonitor.Agent`).
  - **مؤشرات أداء تنفيذية (KPI Cards)**: ملخص فوري لزمن العمل النشط، زمن الخمول، التطبيق الأكثر استخدامًا، وعدد مقاطع الفيديو المسجلة.
  - **البحث والتصفية الفورية**: تصفية الأنشطة حسب اسم البرنامج أو العنوان أو المسار بنص البحث اللحظي.


---

## 📁 Locations & Paths

تلتزم جميع مكونات النظام بالمسار الرسمي الموحد `%ProgramData%\ActivityMonitor` طبقًا للمواصفات:

| المكون | المسار الافتراضي |
| :--- | :--- |
| **قاعدة البيانات** | `%ProgramData%\ActivityMonitor\Data\activity.db` |
| **مقاطع الفيديو** | `%ProgramData%\ActivityMonitor\Recordings\{MachineId}\{YYYY}\{MM}\{DD}\{HH}\{YYYY-MM-DD_HH-mm-ss}.mp4` |
| **التقارير اليومية** | `%ProgramData%\ActivityMonitor\Reports\{MachineId}\{YYYY}\{MM}\{YYYY-MM-DD}.xlsx` |
| **السجلات التقنية** | `%ProgramData%\ActivityMonitor\Logs\agent-{YYYY-MM-dd}.log` و `service-{YYYY-MM-dd}.log` |

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
    "PollingIntervalMs": 500,
    "IdleThresholdMinutes": 5,
    "WindowDebounceMs": 300
  },
  "Recording": {
    "Enabled": true,
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
    "DailyReportScheduleTime": "23:55:00"
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
- إنشاء المجلدات في `C:\Program Files\ActivityMonitor` ومجلدات `%ProgramData%\ActivityMonitor`.
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
   تم الالتزام بعدم تسجيل أي ضغطات للمفاتيح (Keystrokes)، أو كلمات المرور، أو محتويات الحافظة (Clipboard)، أو تسجيل الصوت/الميكروفون أو الكاميرا.
3. **صلاحيات النوافذ المرتفعة (Elevated Windows)**:
   تم استخدام `QueryFullProcessImageName` للتعامل مع البرامج التي تعمل بصلاحيات Administrator حتى يتم استخراج مساراتها التنفيذية دون التعرض لخطأ `Access Denied`.
