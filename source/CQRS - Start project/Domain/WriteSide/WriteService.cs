﻿using Domain.WriteSide.Commands;
using System.Reflection;

namespace Domain.WriteSide;

public class WriteService : IWriteService
{
    private readonly IOrderRepository _repository;
    private readonly IEventStore _eventStore;
    private readonly Dictionary<Type, Action<ICommand>> _commandHandlers = new();

    public WriteService(IOrderRepository repository, IEventStore eventStore)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));

        ScanAssembly();
    }

    private void ScanAssembly()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var handlers = assembly.GetTypes()
            .SelectMany(t => t.GetInterfaces(), (t, i) => new { Type = t, Interface = i })
            .Where(x => x.Interface.IsGenericType && x.Interface.GetGenericTypeDefinition() == typeof(IHandleCommand<>))
            .Select(x => new
            {
                CommandType = x.Interface.GetGenericArguments()[0],
                AggregateType = x.Type
            }).ToList();

        Console.WriteLine("\r\nFound command handlers:");

        var method = this.GetType().GetMethod(nameof(AddCommandHandlerFor))!;

        foreach (var h in handlers)
        {
            Console.WriteLine(h.CommandType + " " + h.AggregateType);

            method.MakeGenericMethod(h.CommandType, h.AggregateType).Invoke(this, Array.Empty<object>());
        }
    }

    public void AddCommandHandlerFor<TCommand, THandler>()
        where TCommand : ICommand where THandler : IHandleCommand<TCommand>
    {
        var handler = (THandler)Activator.CreateInstance(typeof(THandler), _repository);

        _commandHandlers.Add(typeof(TCommand), c =>
        {
            var events = _eventStore.GetEventsForAggregate(c.Id).ToList();
            var eventsLoaded = events.Count;

            var newEvents = handler!.Handle((TCommand)c).ToList();

            Console.WriteLine("\r\nNew events:");

            foreach (var e in newEvents)
            {

                Console.WriteLine(e.ToString());
            }

            if (newEvents.Any())
            {
                _eventStore.SaveEvents(c.Id, eventsLoaded, newEvents);
            }
        });
    }

    public void HandleCommand<TCommand>(TCommand command) where TCommand : ICommand
    {
        Console.WriteLine("Handling command: " + typeof(TCommand).Name);

        if (_commandHandlers.ContainsKey(typeof(TCommand)))
        {
            var handler = _commandHandlers[typeof(TCommand)];
            handler(command);
        }
        else
        {
            throw new Exception("No handler found for command: " + typeof(TCommand).Name);
        }
    }
}