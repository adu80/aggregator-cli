﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using aggregator;
using aggregator.Engine;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using NSubstitute;

using unittests_ruleng.TestData;

using Xunit;

namespace unittests_ruleng
{
    public class WorkItemStoreTests
    {
        private readonly IAggregatorLogger logger;
        private readonly WorkItemTrackingHttpClient witClient;
        private readonly TestClientsContext clientsContext;

        public WorkItemStoreTests()
        {
            logger = Substitute.For<IAggregatorLogger>();

            clientsContext = new TestClientsContext();

            witClient = clientsContext.WitClient;
            witClient.ExecuteBatchRequest(default).ReturnsForAnyArgs(info => new List<WitBatchResponse>());
        }


        [Fact]
        public void GetWorkItem_ById_Succeeds()
        {
            int workItemId = 42;
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>()
            });

            var context = new EngineContext(clientsContext, clientsContext.ProjectId, clientsContext.ProjectName, logger);
            var sut = new WorkItemStore(context);

            var wi = sut.GetWorkItem(workItemId);

            Assert.NotNull(wi);
            Assert.Equal(workItemId, wi.Id.Value);
        }

        [Fact]
        public void GetWorkItems_ByIds_Succeeds()
        {
            var ids = new [] { 42, 99 };
            witClient.GetWorkItemsAsync(ids, expand: WorkItemExpand.All)
                .ReturnsForAnyArgs(new List<WorkItem>
                {
                    new WorkItem
                    {
                        Id = ids[0],
                        Fields = new Dictionary<string, object>()
                    },
                    new WorkItem
                    {
                        Id = ids[1],
                        Fields = new Dictionary<string, object>()
                    }
                });

            var context = new EngineContext(clientsContext, clientsContext.ProjectId, clientsContext.ProjectName, logger);
            var sut = new WorkItemStore(context);

            var wis = sut.GetWorkItems(ids);

            Assert.NotEmpty(wis);
            Assert.Equal(2, wis.Count);
            Assert.Contains(wis, (x) => x.Id.Value == 42);
            Assert.Contains(wis, (x) => x.Id.Value == 99);
        }

        [Fact]
        public async Task NewWorkItem_Succeeds()
        {
            witClient.ExecuteBatchRequest(default).ReturnsForAnyArgs(info => new List<WitBatchResponse>());
            var context = new EngineContext(clientsContext, clientsContext.ProjectId, clientsContext.ProjectName, logger);
            var sut = new WorkItemStore(context);

            var wi = sut.NewWorkItem("Task");
            wi.Title = "Brand new";
            var save = await sut.SaveChanges(SaveMode.Default, false, false, CancellationToken.None);

            Assert.NotNull(wi);
            Assert.True(wi.IsNew);
            Assert.Equal(1, save.created);
            Assert.Equal(0, save.updated);
            Assert.Equal(-1, wi.Id.Value);
        }

        [Fact]
        public void AddChild_Succeeds()
        {
            var context = new EngineContext(clientsContext, clientsContext.ProjectId, clientsContext.ProjectName, logger);
            int workItemId = 1;
            witClient.GetWorkItemAsync(workItemId, expand: WorkItemExpand.All).Returns(new WorkItem
            {
                Id = workItemId,
                Fields = new Dictionary<string, object>(),
                Relations = new List<WorkItemRelation>
                {
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Forward",
                        Url = $"{clientsContext.WorkItemsBaseUrl}/42"
                    },
                    new WorkItemRelation
                    {
                        Rel = "System.LinkTypes.Hierarchy-Forward",
                        Url = $"{clientsContext.WorkItemsBaseUrl}/99"
                    }
                },
            });

            var sut = new WorkItemStore(context);

            var parent = sut.GetWorkItem(workItemId);
            Assert.Equal(2, parent.Relations.Count);

            var newChild = sut.NewWorkItem("Task");
            newChild.Title = "Brand new";
            parent.Relations.AddChild(newChild);

            Assert.NotNull(newChild);
            Assert.True(newChild.IsNew);
            Assert.Equal(-1, newChild.Id.Value);
            Assert.Equal(3, parent.Relations.Count);
        }

        [Fact]
        public void DeleteWorkItem_Succeeds()
        {
            var context = new EngineContext(clientsContext, clientsContext.ProjectId, clientsContext.ProjectName, logger);
            var workItem = ExampleTestData.WorkItem;
            int workItemId = workItem.Id.Value;

            var sut = new WorkItemStore(context, workItem);

            var wrapper = sut.GetWorkItem(workItemId);
            Assert.False(wrapper.IsDeleted);
            Assert.Equal(RecycleStatus.NoChange, wrapper.RecycleStatus);

            sut.DeleteWorkItem(wrapper);
            Assert.True(wrapper.IsDirty);
            Assert.Equal(RecycleStatus.ToDelete, wrapper.RecycleStatus);

            var changedWorkItems = context.Tracker.GetChangedWorkItems();
            Assert.Single(changedWorkItems.Deleted);
            Assert.Empty(changedWorkItems.Created);
            Assert.Empty(changedWorkItems.Updated);
            Assert.Empty(changedWorkItems.Restored);
        }

        [Fact]
        public void DeleteAlreadyDeletedWorkItem_NoChange()
        {
            var context = new EngineContext(clientsContext, clientsContext.ProjectId, clientsContext.ProjectName, logger);
            var workItem = ExampleTestData.DeltedWorkItem;
            int workItemId = workItem.Id.Value;

            var sut = new WorkItemStore(context, workItem);

            var wrapper = sut.GetWorkItem(workItemId);
            Assert.True(wrapper.IsDeleted);
            Assert.Equal(RecycleStatus.NoChange, wrapper.RecycleStatus);

            sut.DeleteWorkItem(wrapper);
            Assert.False(wrapper.IsDirty);
            Assert.Equal(RecycleStatus.NoChange, wrapper.RecycleStatus);

            var changedWorkItems = context.Tracker.GetChangedWorkItems();
            Assert.Empty(changedWorkItems.Deleted);
            Assert.Empty(changedWorkItems.Created);
            Assert.Empty(changedWorkItems.Updated);
            Assert.Empty(changedWorkItems.Restored);
        }

        [Fact]
        public void RestoreNotDeletedWorkItem_NoChange()
        {
            var context = new EngineContext(clientsContext, clientsContext.ProjectId, clientsContext.ProjectName, logger);
            var workItem = ExampleTestData.WorkItem;
            int workItemId = workItem.Id.Value;

            var sut = new WorkItemStore(context, workItem);

            var wrapper = sut.GetWorkItem(workItemId);
            Assert.False(wrapper.IsDeleted);
            Assert.Equal(RecycleStatus.NoChange, wrapper.RecycleStatus);

            sut.RestoreWorkItem(wrapper);
            Assert.False(wrapper.IsDirty);
            Assert.Equal(RecycleStatus.NoChange, wrapper.RecycleStatus);

            var changedWorkItems = context.Tracker.GetChangedWorkItems();
            Assert.Empty(changedWorkItems.Deleted);
            Assert.Empty(changedWorkItems.Created);
            Assert.Empty(changedWorkItems.Updated);
            Assert.Empty(changedWorkItems.Restored);
        }
    }
}
