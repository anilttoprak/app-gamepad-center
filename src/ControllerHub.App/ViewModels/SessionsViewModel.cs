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
        _sessionTracker.SessionStarted += async (_, _) => await RefreshDataAsync();
        _sessionTracker.SessionEnded += async (_, _) => await RefreshDataAsync();
        _sessionTracker.ActiveSessionTicked += OnActiveSessionTicked;
    }

    private void OnActiveSessionTicked(object? sender, TimeSpan activeDuration)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            if (Sessions.Count > 0 && Sessions[0].IsActive)
            {
                var first = Sessions[0];
                Sessions[0] = first with { Duration = SessionTimeCalculator.FormatDurationText(activeDuration) };
            }
        });
    }

    public async Task RefreshDataAsync()
    {
        if (_sessionRepository == null) return;

        var sessions = await _sessionRepository.GetRecentSessionsAsync(1000);
        _allSessions = sessions.ToList();

        ApplyFilter(SelectedFilter);
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

        var today = DateTime.Today;
        int diffToMonday = ((int)today.DayOfWeek + 6) % 7;
        var startOfWeek = today.AddDays(-diffToMonday);
        var startOfMonth = new DateTime(today.Year, today.Month, 1);

        var active = _sessionTracker?.ActiveSession;

        // Filtreye göre tamamlanmış oturumları seç (Aktif oturum s.EndedAtUtc == null olduğu için geçmiş listesine girmez)
        var filtered = _allSessions.Where(s =>
        {
            if (!s.EndedAtUtc.HasValue) return false;

            var sDate = s.StartedAtUtc.ToLocalTime().Date;
            return filter switch
            {
                "Bugün" => sDate == today,
                "Bu Hafta" => sDate >= startOfWeek && sDate <= today,
                "Bu Ay" => sDate >= startOfMonth && sDate <= today,
                _ => true
            };
        }).ToList();

        // Varsa aktif oturumu filtreye uyuyorsa en üste ekle
        if (active != null && active.IsActive)
        {
            var activeDate = active.StartedAtUtc.ToLocalTime().Date;
            bool includeActive = filter switch
            {
                "Bugün" => activeDate == today,
                "Bu Hafta" => activeDate >= startOfWeek,
                "Bu Ay" => activeDate >= startOfMonth,
                _ => true
            };

            if (includeActive)
            {
                var duration = active.GetEffectiveDuration(DateTimeOffset.UtcNow);
                Sessions.Add(new SessionDisplayItem(
                    Id: active.Id,
                    Date: "Bugün",
                    StartTime: active.StartedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
                    EndTime: "Devam Ediyor",
                    Duration: SessionTimeCalculator.FormatDurationText(duration),
                    EndReason: "Aktif Oturum",
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

    private void ShowFeedback(string message)
    {
        StatusFeedback = message;
        HasFeedback = true;
    }
}
