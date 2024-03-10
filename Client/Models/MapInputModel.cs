using System.ComponentModel.DataAnnotations;

namespace Options.Models
{
  public class MapInputModel : BaseInputModel
  {
    [Required]
    public virtual string Expression { get; set; }
  }
}
