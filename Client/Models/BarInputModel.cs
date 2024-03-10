using System.ComponentModel.DataAnnotations;

namespace Options.Models
{
  public class BarInputModel : BaseInputModel
  {
    [Required]
    public virtual string ExpressionUp { get; set; }

    [Required]
    public virtual string ExpressionDown { get; set; }
  }
}
