using System.ComponentModel.DataAnnotations;

namespace Options.Models
{
  public class BalanceInputModel : BarInputModel
  {
    [Required]
    public virtual double Price { get; set; }
  }
}
