using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FixHub.Api.Models;
using FixHub.Api.Services;
using Xunit;

namespace FixHub.Tests;

public class OrderStatusRulesTests
{
    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.Cancelled, OrderActor.Client)]
    [InlineData(OrderStatus.New, OrderStatus.Confirmed, OrderActor.Performer)]
    [InlineData(OrderStatus.New, OrderStatus.Cancelled, OrderActor.Performer)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.InProgress, OrderActor.Performer)]
    [InlineData(OrderStatus.InProgress, OrderStatus.Completed, OrderActor.Performer)]
    public void Allowed_transitions_are_accepted(OrderStatus from, OrderStatus to, OrderActor actor)
    {
        Assert.True(OrderStatusRules.CanTransition(from, to, actor));
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.InProgress)]
    [InlineData(OrderStatus.Completed)]
    public void Client_can_cancel_only_new_orders(OrderStatus from)
    {
        Assert.False(OrderStatusRules.CanTransition(from, OrderStatus.Cancelled, OrderActor.Client));
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.InProgress)]
    [InlineData(OrderStatus.Completed)]
    public void Client_cannot_move_order_forward(OrderStatus to)
    {
        foreach (var from in Enum.GetValues<OrderStatus>())
            Assert.False(OrderStatusRules.CanTransition(from, to, OrderActor.Client));
    }

    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.InProgress)]
    [InlineData(OrderStatus.New, OrderStatus.Completed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Completed)]
    public void Statuses_cannot_be_skipped(OrderStatus from, OrderStatus to)
    {
        Assert.False(OrderStatusRules.CanTransition(from, to, OrderActor.Performer));
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed, OrderStatus.New)]
    [InlineData(OrderStatus.InProgress, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Completed, OrderStatus.InProgress)]
    public void Statuses_cannot_go_backwards(OrderStatus from, OrderStatus to)
    {
        foreach (var actor in Enum.GetValues<OrderActor>())
            Assert.False(OrderStatusRules.CanTransition(from, to, actor));
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public void Final_statuses_have_no_transitions(OrderStatus from)
    {
        foreach (var to in Enum.GetValues<OrderStatus>())
            foreach (var actor in Enum.GetValues<OrderActor>())
                Assert.False(OrderStatusRules.CanTransition(from, to, actor));
    }

    [Fact]
    public void Same_status_is_not_a_transition()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
            foreach (var actor in Enum.GetValues<OrderActor>())
                Assert.False(OrderStatusRules.CanTransition(status, status, actor));
    }

    [Fact]
    public void Next_statuses_for_performer_on_new_order()
    {
        var next = OrderStatusRules.NextStatuses(OrderStatus.New, OrderActor.Performer);

        Assert.Equal(2, next.Count);
        Assert.Contains(OrderStatus.Confirmed, next);
        Assert.Contains(OrderStatus.Cancelled, next);
    }

    [Fact]
    public void Next_statuses_for_client_on_new_order()
    {
        var next = OrderStatusRules.NextStatuses(OrderStatus.New, OrderActor.Client);

        Assert.Single(next);
        Assert.Equal(OrderStatus.Cancelled, next[0]);
    }

    [Fact]
    public void Client_has_no_next_statuses_after_confirmation()
    {
        Assert.Empty(OrderStatusRules.NextStatuses(OrderStatus.Confirmed, OrderActor.Client));
    }
}