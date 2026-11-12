using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerHub.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public abstract string Title { get; }
}
