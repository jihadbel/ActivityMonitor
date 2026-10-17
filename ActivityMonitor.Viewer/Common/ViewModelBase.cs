using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ActivityMonitor.Viewer.Common;

/// <summary>
/// الفئة الأساسية لنماذج العرض (ViewModel Base) التي توفر دعم نمط MVVM وإشعار واجهة المستخدم بالتغييرات.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// الحدث الذي يتم إطلاقه عند تغيير قيمة أي خاصية لإخطار عناصر ربط البيانات (Data Binding) في WPF.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// إطلاق حدث تغيير الخاصية لإخطار واجهة المستخدم بتحديث القيمة المعروضة.
    /// </summary>
    /// <param name="propertyName">اسم الخاصية التي تغيرت قيمتها (يتم جلبه تلقائياً بواسطة CallerMemberName).</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        // التحقق من وجود مشتركين في الحدث قبل إطلاقه
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// تحديث قيمة الحقل الداخلي للخاصية وإطلاق حدث التغيير فقط في حال كانت القيمة الجديدة مختلفة عن الحالية.
    /// </summary>
    /// <typeparam name="T">نوع البيانات الخاص بالحقل والخاصية.</typeparam>
    /// <param name="storage">مرجع للحقل الداخلي (backing field) الذي يخزن القيمة.</param>
    /// <param name="value">القيمة الجديدة المراد إسنادها للحقل.</param>
    /// <param name="propertyName">اسم الخاصية التي يتم تعديلها.</param>
    /// <returns>صحيح (true) إذا تم تعديل القيمة وإطلاق الإشعار؛ وإلا خطأ (false) إذا كانت القيمة مطابقة.</returns>
    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        // إذا كانت القيمة الجديدة مطابقة للقيمة المخزنة حالياً، لا داعي لتحديث الحقل أو إطلاق الإشعار
        if (Equals(storage, value)) return false;

        // تعيين القيمة الجديدة في الحقل الداخلي
        storage = value;

        // إطلاق إشعار التغيير لواجهة المستخدم لتحديث الربط
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// تنفيذ لنمط الأوامر (ICommand RelayCommand) لربط أحداث النقر والإجراءات في واجهة المستخدم بأساليب ViewModel.
/// </summary>
public class RelayCommand : ICommand
{
    // الإجراء الفعلي المراد تنفيذه عند تفعيل الأمر
    private readonly Action<object?> _execute;

    // الدالة المنطقية للتحقق من إمكانية تنفيذ الأمر في الوقت الحالي
    private readonly Predicate<object?>? _canExecute;

    /// <summary>
    /// إنشاء أمر تفويضي يستقبل معامل كائن (object parameter).
    /// </summary>
    /// <param name="execute">الإجراء المطلوب تنفيذه.</param>
    /// <param name="canExecute">الشرط المحدد لجواز تنفيذ الأمر (اختياري).</param>
    /// <exception cref="ArgumentNullException">يتم رميها إذا كان إجراء التنفيذ فارغاً.</exception>
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        // التأكد من توفير إجراء التنفيذ وعدم تمرير قيمة فارغة
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// إنشاء أمر تفويضي بدون معاملات إضافية.
    /// </summary>
    /// <param name="execute">الإجراء المطلوب تنفيذه بدون معاملات.</param>
    /// <param name="canExecute">الشرط المحدد لجواز تنفيذ الأمر بدون معاملات (اختياري).</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute())
    {
        // يتم تحويل الأفعال الخالية من المعاملات إلى دوال متوافقة مع Action<object?> و Predicate<object?>
    }

    /// <summary>
    /// فحص ما إذا كان الأمر متاحاً للتنفيذ حالياً بناءً على الحالة المعطاة.
    /// </summary>
    /// <param name="parameter">المعامل الممرر من واجهة المستخدم.</param>
    /// <returns>صحيح (true) إذا كان الأمر قابلاً للتنفيذ؛ وإلا خطأ (false).</returns>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// تنفيذ الإجراء المقترن بالأمر.
    /// </summary>
    /// <param name="parameter">المعامل الممرر من واجهة المستخدم.</param>
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// حدث إعادة فحص إمكانية تنفيذ الأمر من قبل مدير أوامر WPF.
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// إجبار مدير الأوامر على إعادة تقييم صلاحية تنفيذ الأوامر (CanExecute) فوراً.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}
