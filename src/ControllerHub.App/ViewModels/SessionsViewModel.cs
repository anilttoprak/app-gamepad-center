using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerHub.App.ViewModels;

public record MockSessionItem(long Id, string Date, string StartTime, string EndTime, string Duration, string EndReason, string Status);

public partial class SessionsViewModel : ViewModelBase
{
    public override string Title => "Oturum Geçmişi";

    [ObservableProperty]
    private string _selectedFilter = "Tümü";

    [ObservableProperty]
    private string _totalRecordedSessionsText = "Toplam 28 kayıt bulundu";

    public ObservableCollection<MockSessionItem> Sessions { get; } = new()
    {
        new(28, "12 Kas 2026", "18:20", "20:05", "1 sa 45 dk", "Disconnected", "Tamamlandı"),
        new(27, "11 Kas 2026", "21:10", "23:40", "2 sa 30 dk", "Disconnected", "Tamamlandı"),
        new(26, "10 Kas 2026", "19:00", "19:45", "45 dk", "Timeout / Disconnected", "Tamamlandı"),
        new(25, "09 Kas 2026", "15:30", "18:00", "2 sa 30 dk", "Disconnected", "Tamamlandı"),
        new(24, "08 Kas 2026", "20:00", "22:15", "2 sa 15 dk", "Disconnected", "Tamamlandı"),
        new(23, "07 Kas 2026", "14:10", "17:50", "3 sa 40 dk", "Disconnected", "Tamamlandı"),
        new(22, "06 Kas 2026", "19:30", "20:45", "1 sa 15 dk", "Disconnected", "Tamamlandı"),
        new(21, "05 Kas 2026", "18:00", "19:10", "1 sa 10 dk", "Disconnected", "Tamamlandı"),
        new(20, "04 Kas 2026", "21:00", "22:00", "1 sa 00 dk", "Disconnected", "Tamamlandı")
    };
}
