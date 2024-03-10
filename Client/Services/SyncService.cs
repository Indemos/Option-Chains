using Distribution.ServiceSpace;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Timers;
using Terminal.Connector.Ameritrade;
using Terminal.Core.Models;

namespace Options.Services
{
  public class SyncService
  {
    public Adapter Connector { get; set; }
    public IConfiguration Configuration { get; set; }
    public IList<string> Assets { get; set; } = Array.Empty<string>();
    public IDictionary<string, IList<OptionModel>> Options { get; set; } = new ConcurrentDictionary<string, IList<OptionModel>>();

    public SyncService(IConfiguration configuration)
    {
      Configuration = configuration;
      Assets = new[] { "SPY", "AAPL", "MSFT", "GOOG", "TSLA", "NVDA" };
    }

    public void Create()
    {
      Connector = new Adapter
      {
        ConsumerKey = Configuration.GetValue<string>("Tda:ConsumerKey"),
        Username = Configuration.GetValue<string>("Tda:Username"),
        Password = Configuration.GetValue<string>("Tda:Password"),
        Answer = Configuration.GetValue<string>("Tda:Answer")
      };

      var interval = new Timer(TimeSpan.FromMinutes(1));
      var scheduler = InstanceService<ScheduleService>.Instance;

      scheduler.Send(async () =>
      {
        await Connector.Connect();
        foreach (var asset in Assets)
        {
          Options[asset] = (await Update(asset)).Data;
        }
      });

      interval.Enabled = true;
      interval.Elapsed += (sender, e) => scheduler.Send(async () =>
      {
        await Connector.Connect();
        foreach (var asset in Assets)
        {
          Options[asset] = (await Update(asset)).Data;
        }
      });
    }

    public async Task<ResponseItemModel<IList<OptionModel>>> Update(string asset)
    {
      var date = DateTime.Now;
      var options = await Connector.GetOptions(new OptionMessageModel
      {
        MinDate = date,
        MaxDate = date.AddYears(5),
        Name = asset
      });

      return options;
    }
  }
}
