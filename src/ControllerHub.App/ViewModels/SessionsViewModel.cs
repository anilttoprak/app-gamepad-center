using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.Export;
using ControllerHub.Infrastructure.Services;
using Microsoft.Win32;

namespace ControllerHub.App.ViewModels;

public record SessionDisplayItem(
    long Id,
    int RowNumber,
    string Date,
    string StartTime,
    string EndTime,
    string Duration,
    string EndReason,
    string Status,
    bool IsActive);

public partial class SessionsViewModel : ViewModelBase
{
    private ISessionRepository? _sessionRepository;
    private SessionTrackerService? _sessionTracker;
    private List<ControllerSession> _allSessions = new();

    public override string Title => "Oturum Geçmişi";

    public ObservableCollection<ControllerFilterOption> ControllerFilterOptions { get; } = new();

    private readonly object _collectionsLock = new();

    public SessionsViewModel()
    {
        try
        {
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(Sessions, _collectionsLock);
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(ControllerFilterOptions, _collectionsLock);
        }
        catch { }
    }

    [ObservableProperty]
    private ControllerFilterOption? _selectedControllerFilter;

    public event EventHandler<ControllerFilterOption>? ControllerFilterChanged;
    private bool _isUpdatingFilter = false;

    partial void OnSelectedControllerFilterChanged(ControllerFilterOption? value)
    {
        if (_isUpdatingFilter) return;
        if (value != null)
        {
            ControllerFilterChanged?.Invoke(this, value);
        }
        ApplyFilter(SelectedFilter);
    }

    public void UpdateFilterOptions(IEnumerable<ControllerFilterOption> options, string selectedKey)
    {
        void Update()
        {
            _isUpdatingFilter = true;
            try
            {
                ControllerFilterOptions.Clear();
                foreach (var opt in options)
                {
                    ControllerFilterOptions.Add(opt);
                }

                SelectedControllerFilter = ControllerFilterOptions.FirstOrDefault(o => o.DeviceKey == selectedKey)
                    ?? ControllerFilterOptions.FirstOrDefault();
            }
            finally
            {
                _isUpdatingFilter = false;
            }

            ApplyFilter(SelectedFilter);
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

    [ObservableProperty]
    private string _selectedFilter = "Tümü";

    [ObservableProperty]
    private string _totalRecordedSessionsText = "0 kayıt";

    [ObservableProperty]
    private bool _hasSessions = false;

    [ObservableProperty]
    private bool _isEmptySessions = true;

    [ObservableProperty]
    private string _statusFeedback = "";

    [ObservableProperty]
    private bool _hasFeedback = false;

    public ObservableCollection<SessionDisplayItem> Sessions { get; } = new();

    public void Initialize(ISessionRepository sessionRepository, SessionTrackerService sessionTracker)
    {
        _sessionRepository = sessionRepository;
        _sessionTracker = sessionTracker;
        _sessionTracker.SessionStarted += (_, _) =>
        {
            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(async () => await RefreshDataAsync());
            }
            else
            {
                _ = RefreshDataAsync();
            }
        };
        _sessionTracker.SessionEnded += (_, _) =>
        {
            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(async () => await RefreshDataAsync());
            }
            else
            {
                _ = RefreshDataAsync();
            }
        };
        _sessionTracker.ActiveSessionTicked += OnActiveSessionTicked;
    }

    private void OnActiveSessionTicked(object? sender, TimeSpan activeDuration)
    {
        void Update()
        {
            var activeList = _sessionTracker?.ActiveSessions;
            if (activeList != null && activeList.Count > 0)
            {
                var nowUtc = DateTimeOffset.UtcNow;
                for (int i = 0; i < Sessions.Count; i++)
                {
                    if (!Sessions[i].IsActive) break;
                    var item = Sessions[i];
                    var matched = activeList.FirstOrDefault(a => a.Id == item.Id) ?? (i < activeList.Count ? activeList[i] : null);
                    if (matched != null)
                    {
                        var dur = matched.GetEffectiveDuration(nowUtc);
                        Sessions[i] = item with { Duration = SessionTimeCalculator.FormatDurationText(dur) };
                    }
                }
            }
            else if (Sessions.Count > 0 && Sessions[0].IsActive)
            {
                var first = Sessions[0];
                Sessions[0] = first with { Duration = SessionTimeCalculator.FormatDurationText(activeDuration) };
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Update);
        }
        else
        {
            Update();
        }
    }

    public async Task RefreshDataAsync()
    {
        if (_sessionRepository == null) return;

        var sessions = await _sessionRepository.GetRecentSessionsAsync(1000);
        _allSessions = sessions.ToList();

        void UpdateUi()
        {
            ApplyFilter(SelectedFilter);
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(UpdateUi);
        }
        else
        {
            UpdateUi();
        }
    }

    [RelayCommand]
    private void SetFilter(string filter)
    {
        SelectedFilter = filter;
        ApplyFilter(filter);
    }

    private void ApplyFilter(string filter)
    {
        Sessions.Clear();

        var selectedKey = SelectedControllerFilter?.DeviceKey ?? "ALL";

        var today = DateTime.Today;
        int diffToMonday = ((int)today.DayOfWeek + 6) % 7;
        var startOfWeek = today.AddDays(-diffToMonday);
        var startOfMonth = new DateTime(today.Year, today.Month, 1);

        var active = _sessionTracker?.ActiveSession;
        var activeList = _sessionTracker?.ActiveSessions ?? (active != null && active.IsActive ? new[] { active } : (IReadOnlyList<ControllerSession>)Array.Empty<ControllerSession>());

        // Filtreye göre tamamlanmış oturumları seç (Aktif oturum veya 0sn hayalet kurtarma kayıtları listeye girmez)
        var filtered = _allSessions.Where(s =>
        {
            if (!s.EndedAtUtc.HasValue) return false;
            if (activeList.Any(a => a.Id == s.Id)) return false;
            if (s.DurationSeconds <= 0 && s.EndReason == SessionEndReason.Recovery) return false;
            if (selectedKey != "ALL" && s.DeviceKey != selectedKey) return false;

            var sDate = s.StartedAtUtc.ToLocalTime().Date;
            return filter switch
            {
                "Bugün" => sDate == today,
                "Bu Hafta" => sDate >= startOfWeek && sDate <= today,
                "Bu Ay" => sDate >= startOfMonth && sDate <= today,
                _ => true
            };
        }).ToList();

        int rowNumber = 1;

        // Varsa aktif oturumları filtreye uyuyorsa en üste ekle
        foreach (var act in activeList)
        {
            if (act == null || !act.IsActive) continue;
            if (selectedKey != "ALL" && act.DeviceKey != selectedKey) continue;

            var activeDate = act.StartedAtUtc.ToLocalTime().Date;
            bool includeActive = filter switch
            {
                "Bugün" => activeDate == today,
                "Bu Hafta" => activeDate >= startOfWeek,
                "Bu Ay" => activeDate >= startOfMonth,
                _ => true
            };

            if (includeActive)
            {
                var duration = act.GetEffectiveDuration(DateTimeOffset.UtcNow);
                var deviceLabel = act.DeviceKey.Contains("XINPUT")
                    ? "Aktif Kol (Kablolu / USB)"
                    : (activeList.Count > 1 ? $"Aktif Kol ({act.DeviceName})" : "Aktif Oturum");

                Sessions.Add(new SessionDisplayItem(
                    Id: act.Id,
                    RowNumber: rowNumber++,
                    Date: "Bugün",
                    StartTime: act.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
                    EndTime: "Devam Ediyor",
                    Duration: SessionTimeCalculator.FormatDurationText(duration),
                    EndReason: deviceLabel,
                    Status: "🟢 Devam Ediyor",
                    IsActive: true));
            }
        }

        foreach (var s in filtered)
        {
            var startLocal = s.StartedAtUtc.ToLocalTime();
            var endLocal = s.EndedAtUtc!.Value.ToLocalTime();

            var dateStr = startLocal.Date == today ? "Bugün" :
                          startLocal.Date == today.AddDays(-1) ? "Dün" :
                          startLocal.ToString("dd MMM yyyy");

            var endStr = endLocal.ToString("HH:mm:ss");
            var durationStr = SessionTimeCalculator.FormatDurationText(TimeSpan.FromSeconds(s.DurationSeconds));

            var reasonStr = s.EndReason switch
            {
                SessionEndReason.Disconnected => "Kullanıcı Kapattı / Koptu",
                SessionEndReason.AppExit => "Uygulama Kapanışı",
                SessionEndReason.Recovery => "Çökme / Kurtarma",
                _ => "Bilinmiyor"
            };

            Sessions.Add(new SessionDisplayItem(
                Id: s.Id,
                RowNumber: rowNumber++,
                Date: dateStr,
                StartTime: startLocal.ToString("HH:mm:ss"),
                EndTime: endStr,
                Duration: durationStr,
                EndReason: reasonStr,
                Status: "Tamamlandı",
                IsActive: false));
        }

        TotalRecordedSessionsText = $"Toplam {Sessions.Count} kayıt bulundu";
        HasSessions = Sessions.Count > 0;
        IsEmptySessions = !HasSessions;
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_allSessions.Count == 0)
        {
            ShowFeedback("⚠️ Dışa aktarılacak kayıtlı oturum bulunamadı.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Oturum Geçmişini Kaydet",
            Filter = "CSV Dosyası (*.csv)|*.csv",
            FileName = $"Oturum_Gecmisi_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            var completedSessions = _allSessions.Where(s => s.EndedAtUtc.HasValue);
            var csv = CsvExportService.GenerateSessionsCsv(completedSessions);
            await CsvExportService.SaveCsvFileAsync(dialog.FileName, csv);
            ShowFeedback($"✅ Oturum geçmişi '{Path.GetFileName(dialog.FileName)}' olarak kaydedildi!");
        }
    }

    public event EventHandler? SessionsModified;

    [RelayCommand]
    private async Task DeleteSessionAsync(SessionDisplayItem? item)
    {
        if (item == null || item.IsActive || _sessionRepository == null) return;

        var result = System.Windows.MessageBox.Show(
            $"#{item.Id} numaralı oturum kaydını ({item.Date} - {item.Duration}) silmek istediğinize emin misiniz?",
            "Oturumu Sil",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        var success = await _sessionRepository.DeleteSessionAsync(item.Id);
        if (success)
        {
            await RefreshDataAsync();
            SessionsModified?.Invoke(this, EventArgs.Empty);
            ShowFeedback($"🗑️ #{item.Id} numaralı oturum kaydı silindi.");
        }
    }

    [RelayCommand]
    private async Task ClearSessionsAsync()
    {
        if (_sessionRepository == null || _allSessions.Count == 0)
        {
            ShowFeedback("⚠️ Temizlenecek kayıtlı oturum bulunmuyor.");
            return;
        }

        var selectedKey = SelectedControllerFilter?.DeviceKey ?? "ALL";
        var controllerName = SelectedControllerFilter?.DisplayName ?? "Tüm Kontrolcüler";

        var prompt = selectedKey == "ALL"
            ? "Tüm kontrolcülerin tamamlanan oturum geçmişi kalıcı olarak silinecek. Emin misiniz?"
            : $"'{controllerName}' kontrolcüsüne ait tüm oturum geçmişi kalıcı olarak silinecek. Emin misiniz?";

        var result = System.Windows.MessageBox.Show(
            prompt,
            "Oturum Geçmişini Temizle",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        var count = await _sessionRepository.ClearCompletedSessionsAsync(selectedKey);
        await RefreshDataAsync();
        SessionsModified?.Invoke(this, EventArgs.Empty);
        ShowFeedback($"🗑️ Toplam {count} oturum kaydı başarıyla silindi.");
    }

    private CancellationTokenSource? _feedbackCts;

    [RelayCommand]
    private void DismissFeedback()
    {
        _feedbackCts?.Cancel();
        HasFeedback = false;
        StatusFeedback = "";
    }

    private void ShowFeedback(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        var token = _feedbackCts.Token;

        StatusFeedback = message;
        HasFeedback = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4000, token);
                if (!token.IsCancellationRequested)
                {
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                    {
                        _ = dispatcher.BeginInvoke(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                HasFeedback = false;
                                StatusFeedback = "";
                            }
                        });
                    }
                    else
                    {
                        HasFeedback = false;
                        StatusFeedback = "";
                    }
                }
            }
            catch { }
        });
    }
}
