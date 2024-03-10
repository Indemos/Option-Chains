using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Options.Pages
{
  public partial class Description
  {
    [CascadingParameter] MudDialogInstance Popup { get; set; }

    protected virtual void OnClose() => Popup.Cancel();
  }
}
