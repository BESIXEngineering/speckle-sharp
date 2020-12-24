﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using Speckle.Core.Api;
using Speckle.Core.Logging;
using Speckle.DesktopUI.Utils;
using Stylet;

namespace Speckle.DesktopUI.Streams
{
  public class StreamUpdateObjectsDialogViewModel : StreamDialogBase,
    IHandle<RetrievedFilteredObjectsEvent>, IHandle<UpdateSelectionCountEvent>
  {
    private readonly IEventAggregator _events;
    private ISnackbarMessageQueue _notifications = new SnackbarMessageQueue(TimeSpan.FromSeconds(5));

    public StreamUpdateObjectsDialogViewModel(
      IEventAggregator events,
      StreamsRepository streamsRepo,
      ConnectorBindings bindings)
    {
      DisplayName = "Update Stream";
      _events = events;
      _streamsRepo = streamsRepo;
      Bindings = bindings;

      Roles = new BindableCollection<StreamRole>(_streamsRepo.GetRoles());
      FilterTabs = new BindableCollection<FilterTab>(Bindings.GetSelectionFilters().Select(f => new FilterTab(f)));

      _events.Subscribe(this);
    }

    public void SetState(StreamState state)
    {
      StreamState = state;

      //set previous selection
      if (StreamState.Filter != null)
      {
        SelectedFilterTab = FilterTabs.First(x => x.Name == StreamState.Filter.Name);
        SelectedFilterTab.ListItems = new BindableCollection<string>(StreamState.Filter.Selection);

        if(StreamState.Filter is PropertySelectionFilter stateFilter && SelectedFilterTab.Filter is PropertySelectionFilter selectedFilter)
        {
          selectedFilter.PropertyName = stateFilter.PropertyName;
          selectedFilter.PropertyOperator = stateFilter.PropertyOperator;
          selectedFilter.PropertyValue = stateFilter.PropertyValue;
        }
        
      }
    }

    public bool EditingDetails
    {
      get => SelectedSlide == 0;
    }

    public bool EditingObjects
    {
      get => SelectedSlide == 1;
    }

    public bool EditingCollabs
    {
      get => SelectedSlide == 2;
    }

    private readonly StreamsRepository _streamsRepo;

    public ISnackbarMessageQueue Notifications
    {
      get => _notifications;
      set => SetAndNotify(ref _notifications, value);
    }

    private StreamState _streamState;

    public StreamState StreamState
    {
      get => _streamState;
      set
      {
        SetAndNotify(ref _streamState, value);
      }
    }


    private bool _updateButtonLoading;

    public bool UpdateButtonLoading
    {
      get => _updateButtonLoading;
      set => SetAndNotify(ref _updateButtonLoading, value);
    }


    public void HandleSelectionChanged(ListBox sender, SelectionChangedEventArgs e)
    {

      if (e.AddedItems.Count == 1)
      {
        var toAdd = (string)e.AddedItems[0];
        if (SelectedFilterTab.SelectedListItems.Contains(toAdd))
          return;
        SelectedFilterTab.SelectedListItems.Add(toAdd);
      }

      if (e.RemovedItems.Count == 1)
      {
        var toRemove = (string)e.RemovedItems[0];
        //if it was removed as a result of a search query change, don't actually remove it from our selected list
        if (!SelectedFilterTab.SelectedListItems.Contains(toRemove) || SelectedFilterTab.ListItems.Contains(toRemove))
          return;
        SelectedFilterTab.SelectedListItems.Remove(toRemove);
      }
      //this is to trigger the binding, the Text property doesn't react to INotifyCollectionChanged events!
      SelectedFilterTab.SelectedListItems = SelectedFilterTab.SelectedListItems;
      e.Handled = true;
    }

    public void ClearSelected()
    {
      SelectedFilterTab.ListItems?.Clear();
    }

    public async void UpdateStreamObjects()
    {
      UpdateButtonLoading = true;
      Tracker.TrackPageview("stream", "objects-changed");
      var filter = SelectedFilterTab.Filter;

      filter.Selection = SelectedFilterTab.SelectedListItems.ToList();

      StreamState.Filter = filter;
      Bindings.PersistAndUpdateStreamInFile(StreamState);
      _events.Publish(new StreamUpdatedEvent(StreamState.Stream));
      UpdateButtonLoading = false;
      CloseDialog();
    }

    public void UpdateFromSelection()
    {
      UpdateButtonLoading = true;
      Tracker.TrackPageview("stream", "from-selection");
      SelectedFilterTab = FilterTabs.First(tab => tab.Filter.Name == "Selection");
      SelectedFilterTab.SelectedListItems.Clear();
      SelectedFilterTab.Filter.Selection = Bindings.GetSelectedObjects();

      UpdateStreamObjects();
    }

    public void UpdateFromView()
    {
      Tracker.TrackPageview("stream", "from-view");
      SelectedFilterTab = FilterTabs.First(tab => tab.Filter.Name == "Selection");
      SelectedFilterTab.Filter.Selection = ActiveViewObjects;

      UpdateStreamObjects();
    }

    public async void AddCollaboratorsToStream()
    {
      if (Role == null)
      {
        Notifications.Enqueue("Please select a role");
        return;
      }

      if (!Collaborators.Any()) return;
      Tracker.TrackPageview("stream", "collaborators");
      var success = 0;
      foreach (var collaborator in Collaborators)
      {
        try
        {
          var res = await StreamState.Client.StreamGrantPermission(new StreamGrantPermissionInput()
          {
            role = Role.Role,
            streamId = StreamState.Stream.id,
            userId = collaborator.id
          });
          if (res) success++;
        }
        catch (Exception e)
        {
          Log.CaptureException(e);
          Notifications.Enqueue($"Failed to add collaborators: {e}");
          return;
        }
      }

      if (success == 0)
      {
        Notifications.Enqueue("Could not add collaborators to this stream");
        return;
      }

      StreamState.Stream = await StreamState.Client.StreamGet(StreamState.Stream.id);
      Collaborators.Clear();
      _events.Publish(new StreamUpdatedEvent(StreamState.Stream));
      Notifications.Enqueue($"Added {success} collaborators to this stream");
    }

    public async void RemoveCollaborator(Collaborator collaborator)
    {
      Tracker.TrackPageview("stream", "collaborators");
      try
      {
        var res = await StreamState.Client.StreamRevokePermission(new StreamRevokePermissionInput()
        {
          streamId = StreamState.Stream.id,
          userId = collaborator.id
        });
        if (!res)
        {
          Notifications.Enqueue($"Could not revoke {collaborator.name}'s permissions");
          return;
        }
      }
      catch (Exception e)
      {
        Log.CaptureException(e);
        Notifications.Enqueue($"Could not revoke {collaborator.name}'s permissions: {e}");
        return;
      }

      StreamState.Stream = await StreamState.Client.StreamGet(StreamState.Stream.id);
      _events.Publish(new StreamUpdatedEvent(StreamState.Stream));
      Notifications.Enqueue($"Revoked {collaborator.name}'s permissions");
    }

    public void Handle(RetrievedFilteredObjectsEvent message)
    {
      StreamState.Objects = message.Objects.ToList();
    }

    public void Handle(UpdateSelectionCountEvent message)
    {
      SelectionCount = message.SelectionCount;
    }
  }
}
