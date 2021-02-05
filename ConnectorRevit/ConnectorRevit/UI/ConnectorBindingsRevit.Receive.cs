﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using ConnectorRevit;
using ConnectorRevit.Revit;
using Speckle.ConnectorRevit.Storage;
using Speckle.Core.Api;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using Speckle.Core.Models;
using Speckle.Core.Transports;
using Speckle.DesktopUI.Utils;
using Stylet;
using RevitElement = Autodesk.Revit.DB.Element;

namespace Speckle.ConnectorRevit.UI
{
  public partial class ConnectorBindingsRevit
  {


    /// <summary>
    /// Receives a stream and bakes into the existing revit file.
    /// </summary>
    /// <param name="state"></param>
    /// <returns></returns>
    public override async Task<StreamState> ReceiveStream(StreamState state)
    {
      ConversionErrors.Clear();
      OperationErrors.Clear();

      var kit = KitManager.GetDefaultKit();
      var converter = kit.LoadConverter(ConnectorRevitUtils.RevitAppName);
      converter.SetContextDocument(CurrentDoc.Document);
      var previouslyReceiveObjects = state.ReceivedObjects;

      var transport = new ServerTransport(state.Client.Account, state.Stream.id);

      string referencedObject = state.Commit.referencedObject;

      //if "latest", always make sure we get the latest commit when the user clicks "receive"
      if (state.Commit.id == "latest")
      {
        var res = await state.Client.BranchGet(state.CancellationTokenSource.Token, state.Stream.id, state.Branch.name, 1);
        referencedObject = res.commits.items.FirstOrDefault().referencedObject;
      }

      var commit = state.Commit;

      if (state.CancellationTokenSource.Token.IsCancellationRequested)
      {
        return null;
      }

      var commitObject = await Operations.Receive(
          referencedObject,
          state.CancellationTokenSource.Token,
          transport,
          onProgressAction: dict => UpdateProgress(dict, state.Progress),
          onErrorAction: (s, e) =>
          {
            OperationErrors.Add(e);
            state.Errors.Add(e);
            state.CancellationTokenSource.Cancel();
          },
          onTotalChildrenCountKnown: count => Execute.PostToUIThread(() => state.Progress.Maximum = count)
          );

      if (OperationErrors.Count != 0)
      {
        Globals.Notify("Failed to get commit.");
        return state;
      }

      if (state.CancellationTokenSource.Token.IsCancellationRequested)
      {
        return null;
      }

      UpdateProgress(new ConcurrentDictionary<string, int>() { ["Converting"] = 1 }, state.Progress);

      // Bake the new ones.
      Queue.Add(() =>
      {
        using (var t = new Transaction(CurrentDoc.Document, $"Baking stream {state.Stream.name}"))
        {
          t.Start();
          var failOpts = t.GetFailureHandlingOptions();
          failOpts.SetFailuresPreprocessor(new ErrorEater(converter));
          t.SetFailureHandlingOptions(failOpts);


          var flattenedObjects = FlattenCommitObject(commitObject, converter);
          // needs to be set for editing to work 
          converter.SetPreviousContextObjects(previouslyReceiveObjects);
          // needs to be set for openings in floors and roofs to work
          converter.SetContextObjects(flattenedObjects.Select(x => new ApplicationPlaceholderObject { applicationId = x.applicationId, NativeObject = x }).ToList());
          var newPlaceholderObjects = ConvertReceivedObjects(flattenedObjects, converter, state);

          DeleteObjects(previouslyReceiveObjects, newPlaceholderObjects);

          state.ReceivedObjects = newPlaceholderObjects;
          state.Errors.AddRange(converter.ConversionErrors.Select(e => new Exception($"{e.message}: {e.details}")));

          t.Commit();
        }
      });

      Executor.Raise();

      

      try
      {
        await state.RefreshStream();

        WriteStateToFile();
      }
      catch (Exception e)
      {
        WriteStateToFile();
        state.Errors.Add(e);
        Globals.Notify($"Receiving done, but failed to update stream from server.\n{e.Message}");
      }

      return state;
    }

    //delete previously sent object that are no more in this stream
    private void DeleteObjects(List<ApplicationPlaceholderObject> previouslyReceiveObjects, List<ApplicationPlaceholderObject> newPlaceholderObjects)
    {
      foreach (var obj in previouslyReceiveObjects)
      {
        if (newPlaceholderObjects.Any(x => x.applicationId == obj.applicationId))
          continue;

        var element = CurrentDoc.Document.GetElement(obj.ApplicationGeneratedId);
        if (element != null)
        {
          CurrentDoc.Document.Delete(element.Id);
        }

      }
    }

    private List<ApplicationPlaceholderObject> ConvertReceivedObjects(List<Base> objects, ISpeckleConverter converter, StreamState state)
    {
      var placeholders = new List<ApplicationPlaceholderObject>();

      foreach (var @base in objects)
      {
        try
        {
          var convRes = converter.ConvertToNative(@base);
          if (convRes is ApplicationPlaceholderObject placeholder)
          {
            placeholders.Add(placeholder);
          }
          else if (convRes is List<ApplicationPlaceholderObject> placeholderList)
          {
            placeholders.AddRange(placeholderList);
          }
        }
        catch (Exception e)
        {
          state.Errors.Add(e);
        }
      }

      return placeholders;
    }

    /// <summary>
    /// Recurses through the commit object and flattens it. 
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="converter"></param>
    /// <returns></returns>
    private List<Base> FlattenCommitObject(object obj, ISpeckleConverter converter)
    {
      List<Base> objects = new List<Base>();

      if (obj is Base @base)
      {
        if (converter.CanConvertToNative(@base))
        {
          objects.Add(@base);

          return objects;
        }
        else
        {
          foreach (var prop in @base.GetDynamicMembers())
          {
            objects.AddRange(FlattenCommitObject(@base[prop], converter));
          }
          return objects;
        }
      }

      if (obj is List<object> list)
      {
        foreach (var listObj in list)
        {
          objects.AddRange(FlattenCommitObject(listObj, converter));
        }
        return objects;
      }

      if (obj is IDictionary dict)
      {
        foreach (DictionaryEntry kvp in dict)
        {
          objects.AddRange(FlattenCommitObject(kvp.Value, converter));
        }
        return objects;
      }

      return objects;
    }



  }
}
