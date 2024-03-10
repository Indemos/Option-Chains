using MudBlazor;
using System.ComponentModel.DataAnnotations;

namespace Options.Models
{
  public class BaseInputModel
  {
    public virtual string Group { get; set; }

    public virtual string Map { get; set; }

    [Required]
    public virtual string Name { get; set; }

    [Required]
    public virtual DateRange Range { get; set; }
  }
}
