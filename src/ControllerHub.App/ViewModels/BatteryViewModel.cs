using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerHub.App.ViewModels;

public record MockBatteryChangeItem(string Date, int PreviousPercentage, int NewPercentage, string Note, string Source);

public partial class BatteryViewModel : ViewModelBase
{
    public override string Title => "Pil Yönetimi";

    [ObservableProperty]
    private string _currentPercentage = "%85";

    [ObservableProperty]
    private string _lastUpdatedText = "12 Kas 2026 20:09 (Bluetooth GATT 0x180F)";

    [ObservableProperty]
    private int _newBatteryPercentage = 100;

    [ObservableProperty]
    private string _batteryNote = "Duracell Ultra AA (2'li set)";

    public ObservableCollection<MockBatteryChangeItem> ChangeHistory { get; } = new()
    {
        new("02 Kas 2026 14:00", 8, 100, "Duracell Ultra AA Yeni Takıldı", "Manuel"),
        new("15 Eki 2026 19:30", 5, 100, "Eneloop Pro Şarjlı Pil Takıldı", "Manuel")
    };
}
