using Canvas.Core.Composers;
using Canvas.Core.Engines;
using Canvas.Core.Enums;
using Canvas.Core.Models;
using Canvas.Core.Services;
using Canvas.Core.Shapes;
using Canvas.Views.Web.Views;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using MudBlazor;
using Options.Models;
using Options.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Connector.Ameritrade;
using Terminal.Core.Enums;
using Terminal.Core.Extensions;
using Terminal.Core.Models;

namespace Options.Pages
{
  public partial class Index
  {
    [Inject] ISnackbar Snackbar { get; set; }
    [Inject] IDialogService ModalService { get; set; }
    [Inject] IConfiguration Configuration { get; set; }
    [Inject] SyncService DataService { get; set; }

    protected virtual int Count { get; set; } = 1;
    protected virtual bool IsLoading { get; set; }
    protected virtual Dictionary<string, CanvasView> Maps { get; set; } = new();
    protected virtual Dictionary<string, Dictionary<string, CanvasView>> Groups { get; set; } = new();

    /// <summary>
    /// Page setup
    /// </summary>
    /// <param name="setup"></param>
    /// <returns></returns>
    protected override async Task OnAfterRenderAsync(bool setup)
    {
      if (setup)
      {
        DataService.Create();
      }

      await base.OnAfterRenderAsync(setup);
    }

    void OnClear() => Groups.Clear();

    /// <summary>
    /// Description popup
    /// </summary>
    /// <returns></returns>
    async Task OnDescription()
    {
      var props = new DialogOptions
      {
        FullWidth = true,
        MaxWidth = MaxWidth.ExtraSmall,
        CloseOnEscapeKey = true
      };

      var response = await ModalService
        .ShowAsync<Description>("Description", props)
        .ContinueWith(process => 0);
    }

    /// <summary>
    /// Bar chart editor
    /// </summary>
    /// <returns></returns>
    async Task OnBarChart()
    {
      await OnChart<BarEditor>(async (caption, response, options) =>
      {
        var responseData = response as BarInputModel;

        await Group(caption, response.Group, options, async (view, records) =>
        {
          await ShowBars(
            responseData.ExpressionUp,
            responseData.ExpressionDown,
            view,
            records);
        });
      });
    }

    /// <summary>
    /// Area chart editor
    /// </summary>
    /// <returns></returns>
    async Task OnBalanceChart()
    {
      await OnChart<BalanceEditor>(async (caption, response, options) =>
      {
        var responseData = response as BalanceInputModel;

        await Group(caption, response.Group, options, async (view, records) =>
        {
          await ShowBalance(
            responseData.Price,
            responseData.ExpressionUp,
            responseData.ExpressionDown,
            view,
            records);
        });
      });
    }

    /// <summary>
    /// Map chart editor
    /// </summary>
    /// <returns></returns>
    async Task OnMapChart()
    {
      await OnChart<MapEditor>(async (caption, response, options) =>
      {
        var responseData = response as MapInputModel;

        await Group(caption, response.Group, options, async (view, records) =>
        {
          await ShowMaps(
            responseData.Expression,
            view,
            records);
        });
      });
    }

    /// <summary>
    /// Group by expirations or not
    /// </summary>
    /// <param name="caption"></param>
    /// <param name="combine"></param>
    /// <param name="options"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    async Task Group(
      string caption,
      string combine,
      IList<OptionModel> options,
      Func<CanvasView, IList<OptionModel>, Task> action)
    {
      if (string.Equals(combine, "Yes"))
      {
        Groups[caption] = new Dictionary<string, CanvasView> { [caption] = null };
        await InvokeAsync(StateHasChanged);
        Groups[caption].ForEach(async o => await action(o.Value, options));
        return;
      }

      var groups = options
        .GroupBy(o => $"{o.ExpirationDate}", o => o)
        .ToDictionary(o => o.Key, o => o.ToList());

      Groups[caption] = Enumerable
        .Range(0, groups.Count)
        .ToDictionary(o => groups.Keys.ElementAt(o), o => null as CanvasView);

      await InvokeAsync(StateHasChanged);

      Groups[caption].ForEach(async o => await action(o.Value, groups[o.Key]));
    }

    /// <summary>
    /// API query to get options by criteria
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="action"></param>
    /// <returns></returns>
    async Task OnChart<T>(Func<string, BaseInputModel, IList<OptionModel>, Task> action) where T : Microsoft.AspNetCore.Components.ComponentBase
    {
      var response = await ModalService
        .ShowAsync<T>("Editor")
        .ContinueWith(async process =>
        {
          IsLoading = true;

          var popup = await process;
          var response = await popup.Result;

          if (response.Canceled is false)
          {
            var data = response.Data as BaseInputModel;
            var adapter = new Adapter
            {
              ConsumerKey = Configuration.GetValue<string>("Tda:ConsumerKey"),
              Username = Configuration.GetValue<string>("Tda:Username"),
              Password = Configuration.GetValue<string>("Tda:Password"),
              Answer = Configuration.GetValue<string>("Tda:Answer")
            };

            var caption =
              $"{Count++}" + " : " +
              data.Name + " : " +
              data.Range.Start + " : " +
              data.Range.End;

            if (DataService.Options.TryGetValue(data.Name, out var items))
            {
              var rangeItems = items
                .Where(o => o.ExpirationDate >= data.Range.Start)
                .Where(o => o.ExpirationDate <= data.Range.End)
                .ToList();

              await action(caption, data, rangeItems);
            }
            else
            {
              await adapter.Connect();

              var options = await adapter.GetOptions(new OptionMessageModel
              {
                MinDate = data.Range.Start,
                MaxDate = data.Range.End,
                Name = data.Name?.ToUpper()
              });

              await action(caption, data, options.Data);

              DataService.Assets.Add(data.Name);
            }
          }

          IsLoading = false;

          await InvokeAsync(StateHasChanged);
        });
    }

    /// <summary>
    /// Show bar charts
    /// </summary>
    /// <param name="expressionUp"></param>
    /// <param name="expressionDown"></param>
    /// <param name="view"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    protected virtual async Task ShowBars(
      string expressionUp,
      string expressionDown,
      CanvasView view,
      IList<OptionModel> options)
    {
      var groups = options
        .OrderBy(o => o.Strike)
        .GroupBy(o => o.Strike, o => o)
        .ToList();

      var comUp = new ComponentModel { Color = SKColors.DeepSkyBlue };
      var comDown = new ComponentModel { Color = SKColors.OrangeRed };
      var points = groups.Select((group, i) =>
      {
        var ups = group.Sum(o => Compute(expressionUp, o));
        var downs = -group.Sum(o => Compute(expressionDown, o));

        return new GroupShape
        {
          X = i,
          Groups = new Dictionary<string, IGroupShape>
          {
            ["Indicators"] = new GroupShape
            {
              Groups = new Dictionary<string, IGroupShape>
              {
                ["Ups"] = new BarShape { Y = ups, Component = comUp },
                ["Downs"] = new BarShape { Y = downs, Component = comDown }
              }
            }
          }
        } as IShape;

      }).ToList();

      string showIndex(double o)
      {
        var index = Math.Min(Math.Max((int)Math.Round(o), 0), groups.Count - 1);
        var group = groups.ElementAtOrDefault(index) ?? default;
        var price = group.ElementAtOrDefault(0)?.Strike;

        return price is null ? null : $"{price}";
      }

      var composer = new GroupComposer
      {
        Name = "Indicators",
        Items = points,
        ShowIndex = showIndex,
        View = view
      };

      await composer.Create<CanvasEngine>();
      await composer.Update();
    }

    /// <summary>
    /// Show balance charts
    /// </summary>
    /// <param name="expressionUp"></param>
    /// <param name="expressionDown"></param>
    /// <param name="view"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    protected virtual async Task ShowBalance(
      double price,
      string expressionUp,
      string expressionDown,
      CanvasView view,
      IList<OptionModel> options)
    {
      var groups = options
        .OrderBy(o => o.Strike)
        .GroupBy(o => o.Strike, o => o)
        .ToList();

      var index = groups
        .Select((o, i) => new { Index = i, Data = o })
        .FirstOrDefault(o => o.Data.Key > price)
        ?.Index ?? 0;

      if (index is 0)
      {
        index = groups.Count / 2;
      }

      var indexUp = index;
      var indexDown = index;
      var sumUp = 0.0;
      var sumDown = 0.0;
      var points = new IShape[groups.Count];
      var comUp = new ComponentModel { Color = SKColors.DeepSkyBlue };
      var comDown = new ComponentModel { Color = SKColors.OrangeRed };

      for (var i = 0; i < groups.Count; i++)
      {
        if (indexUp < groups.Count)
        {
          var sum = groups.ElementAtOrDefault(indexUp)?.Sum(o => Compute(expressionUp, o)) ?? 0;

          points[indexUp] = new GroupShape
          {
            X = indexUp,
            Groups = new Dictionary<string, IGroupShape>
            {
              ["Indicators"] = new GroupShape
              {
                Groups = new Dictionary<string, IGroupShape>
                {
                  ["Ups"] = new AreaShape { Y = sumUp + sum, Component = comUp }
                }
              }
            }
          } as IShape;

          sumUp += sum;
        }

        if (indexDown >= 0)
        {
          var sum = groups.ElementAtOrDefault(indexDown)?.Sum(o => Compute(expressionDown, o)) ?? 0;

          points[indexDown] = new GroupShape
          {
            X = indexDown,
            Groups = new Dictionary<string, IGroupShape>
            {
              ["Indicators"] = new GroupShape
              {
                Groups = new Dictionary<string, IGroupShape>
                {
                  ["Downs"] = new AreaShape { Y = sumDown + sum, Component = comDown }
                }
              }
            }
          } as IShape;

          sumDown += sum;
        }

        indexUp++;
        indexDown--;
      }

      string showIndex(double o)
      {
        var index = Math.Max(Math.Min((int)Math.Round(o), groups.Count - 1), 0);
        var group = groups.ElementAtOrDefault(index) ?? default;
        var price = group.ElementAtOrDefault(0)?.Strike;

        return price is null ? null : $"{price}";
      }

      var composer = new GroupComposer
      {
        Name = "Indicators",
        Items = points,
        ShowIndex = showIndex,
        View = view
      };

      await composer.Create<CanvasEngine>();
      await composer.Update();
    }

    /// <summary>
    /// Show map chart
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="view"></param>
    /// <param name="options"></param>
    /// <returns></returns>
    protected virtual async Task ShowMaps(string expression, CanvasView view, IList<OptionModel> options)
    {
      var groups = options
        .OrderBy(o => o.Strike)
        .GroupBy(o => o.Strike, o => o)
        .ToDictionary(o => o.Key, o => o
          .OrderBy(option => option.ExpirationDate)
          .GroupBy(option => option.ExpirationDate)
          .ToDictionary(group => group.Key, group => group.ToList()));

      var min = options.Min(o => Compute(expression, o));
      var max = options.Max(o => Compute(expression, o));
      var colorService = new ColorService { Min = min, Max = max, Mode = ShadeEnum.Intensity };
      var expirationMap = new Dictionary<string, DateTime>();
      var points = groups.Select(group =>
      {
        return new ColorMapShape
        {
          Points = group.Value.Select(date =>
          {
            var value = date.Value.Sum(o => Compute(expression, o));
            expirationMap[$"{date.Key}"] = date.Key.Value;
            return new ComponentModel
            {
              Size = value,
              Color = colorService.GetColor(value)
            };

          }).ToList()
        };
      }).ToArray();

      var expirations = expirationMap
        .Values
        .OrderBy(o => o)
        .Select(o => $"{o:yyyy-MM-dd}")
        .ToList();

      string showIndex(double o)
      {
        var index = Math.Min(Math.Max((int)o, 0), points.Length - 1);
        var name = groups.Keys.ElementAtOrDefault(index);
        var caption = groups.Get(name)?.FirstOrDefault().Value.FirstOrDefault()?.Strike;

        return caption?.ToString();
      }

      string showValue(double o)
      {
        var index = Math.Min(Math.Max((int)o, 0), expirations.Count - 1);
        var caption = expirations.ElementAtOrDefault(index);

        return $"{caption}";
      }

      var composer = new MapComposer
      {
        Name = "Indicators",
        Items = points,
        ValueCount = Math.Min(expirations.Count, 5),
        ShowIndex = showIndex,
        ShowValue = showValue,
        View = view
      };

      await composer.Create<CanvasEngine>();
      await composer.Update();
    }

    /// <summary>
    /// Evaluate expression for the option
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="o"></param>
    /// <returns></returns>
    private double Compute(string expression, OptionModel o)
    {
      var ce = new CalcEngine.Core.CalculationEngine();

      try
      {
        ce.Variables["Volume"] = o.Volume;
        ce.Variables["Volatility"] = o.Volatility;
        ce.Variables["OpenInterest"] = o.OpenInterest;
        ce.Variables["IntrinsicValue"] = o.IntrinsicValue;
        ce.Variables["BidSize"] = o.Point.BidSize;
        ce.Variables["AskSize"] = o.Point.AskSize;
        ce.Variables["Vega"] = o.Derivatives.Vega;
        ce.Variables["Gamma"] = o.Derivatives.Gamma;
        ce.Variables["Theta"] = o.Derivatives.Theta;
        ce.Variables["Delta"] = o.Derivatives.Delta;

        if (Equals(o.Side, OptionSideEnum.Put))
        {
          ce.Variables["PVolume"] = o.Volume;
          ce.Variables["PVolatility"] = o.Volatility;
          ce.Variables["POpenInterest"] = o.OpenInterest;
          ce.Variables["PIntrinsicValue"] = o.IntrinsicValue;
          ce.Variables["PBidSize"] = o.Point.BidSize;
          ce.Variables["PAskSize"] = o.Point.AskSize;
          ce.Variables["PVega"] = o.Derivatives.Vega;
          ce.Variables["PGamma"] = o.Derivatives.Gamma;
          ce.Variables["PTheta"] = o.Derivatives.Theta;
          ce.Variables["PDelta"] = o.Derivatives.Delta;

          ce.Variables["CVolume"] = 0.0;
          ce.Variables["CVolatility"] = 0.0;
          ce.Variables["COpenInterest"] = 0.0;
          ce.Variables["CIntrinsicValue"] = 0.0;
          ce.Variables["CBidSize"] = 0.0;
          ce.Variables["CAskSize"] = 0.0;
          ce.Variables["CVega"] = 0.0;
          ce.Variables["CGamma"] = 0.0;
          ce.Variables["CTheta"] = 0.0;
          ce.Variables["CDelta"] = 0.0;
        }

        if (Equals(o.Side, OptionSideEnum.Call))
        {
          ce.Variables["PVolume"] = 0.0;
          ce.Variables["PVolatility"] = 0.0;
          ce.Variables["POpenInterest"] = 0.0;
          ce.Variables["PIntrinsicValue"] = 0.0;
          ce.Variables["PBidSize"] = 0.0;
          ce.Variables["PAskSize"] = 0.0;
          ce.Variables["PVega"] = 0.0;
          ce.Variables["PGamma"] = 0.0;
          ce.Variables["PTheta"] = 0.0;
          ce.Variables["PDelta"] = 0.0;

          ce.Variables["CVolume"] = o.Volume;
          ce.Variables["CVolatility"] = o.Volatility;
          ce.Variables["COpenInterest"] = o.OpenInterest;
          ce.Variables["CIntrinsicValue"] = o.IntrinsicValue;
          ce.Variables["CBidSize"] = o.Point.BidSize;
          ce.Variables["CAskSize"] = o.Point.AskSize;
          ce.Variables["CVega"] = o.Derivatives.Vega;
          ce.Variables["CGamma"] = o.Derivatives.Gamma;
          ce.Variables["CTheta"] = o.Derivatives.Theta;
          ce.Variables["CDelta"] = o.Derivatives.Delta;
        }

        return Convert.ToDouble(ce.Evaluate(expression));
      }
      catch (Exception e)
      {
        Snackbar.Add("Invalid expression: " + e.Message);
        return 0;
      }
    }
  }
}
