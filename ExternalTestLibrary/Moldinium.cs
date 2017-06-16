using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AssemblyToProcess.Moldinium
{
    public static class Tracker
    {
        const Int32 MaxReactionIterations = 100;

        internal class Logger
        {
            public void OpenScope(Object o) { }
            public void CloseScope(Object o = null) { }
            public void Log(Object l, Object r = null) { }
        }

        internal static Logger logger = null;



        internal class EvaluationRecord
        {
            public List<Dependency> dependencies = new List<Dependency>();
        }

        static Boolean isRunningReactions = false;

        static Stack<EvaluationRecord> evaluationStack
            = new Stack<EvaluationRecord>();

        static internal Boolean AreEvaluating
            => evaluationStack.Count > 0 && evaluationStack.Peek() != null;

        static internal Boolean AreInBatch
            => batchCount > 0;

        static internal Int32 batchCount = 0;

        static internal void NoteEvaluation(Dependency dependency)
        {
            if (!AreEvaluating) throw new Exception("NoteEvaluation called outside evaluation.");

            evaluationStack.Peek().dependencies.Add(dependency);
        }

        static internal void OpenEvaluation(String agent, String reason)
        {
            logger?.OpenScope($"evaluation by {agent} {reason}");

            evaluationStack.Push(new EvaluationRecord());
        }

        static internal EvaluationRecord CloseEvaluation()
        {
            var record = evaluationStack.Pop();

            logger?.CloseScope();

            return record;
        }

        static internal void PauseEvaluation()
        {
            evaluationStack.Push(null);
        }

        static internal void ResumeEvaluation()
        {
            evaluationStack.Pop();
        }

        static internal void OpenBatch()
        {
            logger?.OpenScope("batch");

            ++batchCount;
        }

        static internal void CloseBatch()
        {
            if (--batchCount == 0)
            {
                logger?.CloseScope("outermost, now running reactions");

                RunReactionsIfAppropriate();

                logger?.Log("outermost", "reactions done, now relaxing");

                BroadcastRelaxIfAppropriate();

                logger?.Log("outermost", "relaxing completed, batch finished");
            }
            else
            {
                logger?.CloseScope();
            }
        }

        static internal IDisposable Batch()
        {
            OpenBatch();
            return batchCloser;
        }

        class BatchCloser : IDisposable
        {
            public void Dispose() => CloseBatch();
        }

        static BatchCloser batchCloser = new BatchCloser();

        static internal void ScheduleForRelaxation(ITrackingSubscriber subscriber, Int32 index)
        {
            // The subscriber can be a reaction, in which case it's indeed never relaxed.
            var dependency = subscriber as ITrackableObject;

            if (dependency == null) return;

            scheduledForRelaxation.Add(new Dependency(dependency, index));
        }

        static void BroadcastRelaxIfAppropriate()
        {
            while (scheduledForRelaxation.Count > 0)
            {
                var dependency = scheduledForRelaxation[0];
                scheduledForRelaxation.RemoveAt(0);

                dependency.target.Notify(dependency.index, TrackingNotification.RelaxIfAppropriate);
            }
        }

        internal static void ScheduleOrRunReaction(Action reaction)
        {
            reactions.Add(reaction);
            RunReactionsIfAppropriate();
        }

        static void RunReactionsIfAppropriate()
        {
            if (batchCount > 0 || isRunningReactions)
                return;

            isRunningReactions = true;

            try
            {
                var iterations = 0;

                while (reactions.Count > 0)
                {
                    if (++iterations == MaxReactionIterations)
                    {
                        throw new Exception($"Reactions didn't complete after {MaxReactionIterations} cycles.");
                    }

                    var copy = reactions.ToArray();
                    reactions.Clear();

                    foreach (var reaction in copy)
                    {
                        reaction?.Invoke();
                    }
                }
            }
            finally
            {
                isRunningReactions = false;
            }
        }

        static List<Action> reactions = new List<Action>();
        static List<Dependency> scheduledForRelaxation = new List<Dependency>();
    }

    interface ITrackingSubscriber
    {
        void Notify(Int32 index, TrackingNotification notification);

        void Update(Int32 index, String reason);

        String GetPropertyName(Int32 index);
    }

    interface ITrackableObject : ITrackingSubscriber
    {
        void Subscribe(Int32 index, Subscriber subscriber);

        void Unsubscribe(Int32 index, Subscriber subscriber);
    }

    enum TrackingNotification
    {
        Stale,
        ReadyUnmodified,
        ReadyModified,
        RelaxIfAppropriate
    }

    struct TrackableObjectMixIn<Container> : ITrackableObject
    {
        public void Notify(Int32 index, TrackingNotification notification) { }

        public void Update(Int32 index, String reason) { }

        public void Subscribe(Int32 index, Subscriber subscriber) { }

        public void Unsubscribe(Int32 index, Subscriber subscriber) { }

        public String GetPropertyName(Int32 index) { return null; }
    }

    class TrackingData<T>
    {
        public T cachedValue;
        public Exception cachedException;

        public Boolean isKnownToBeStale;

        public Int32 staleDependencies;
        public Boolean haveModifiedDependencies;

        public List<Dependency> dependencies = new List<Dependency>();

        public List<Subscriber> subscribers = new List<Subscriber>();
    }

    struct Subscriber : IEquatable<Subscriber>
    {
        public ITrackingSubscriber target;
        public Int32 index;

        public Subscriber(ITrackingSubscriber target, Int32 index)
        {
            this.target = target;
            this.index = index;
        }

        public Boolean Equals(Subscriber other)
            => target == other.target && index == other.index;

        public override String ToString()
            => null;
    }

    struct Dependency : IEquatable<Dependency>
    {
        public ITrackableObject target;
        public Int32 index;

        public Dependency(ITrackableObject target, Int32 index)
        {
            this.target = target;
            this.index = index;
        }

        public Boolean Equals(Dependency other)
            => target == other.target && index == other.index;

        public override String ToString()
            => null;
    }

    struct TrackingProperty<T, Container, Accessor>
        : IPropertyImplementation<T, ITrackableObject, T, Container, TrackableObjectMixIn<Container>, Accessor>
        where Container : ITrackableObject
        where Accessor : IAccessor<T, Container>
    {
        TrackingData<T> trackingData;

        Accessor accessor;

        public void Subscribe(Container self, ref TrackableObjectMixIn<Container> mixIn, Subscriber subscriber)
        {
            if (trackingData == null)
            {
                trackingData = new TrackingData<T>();

                Update(self, ref mixIn, $"on alert due to subscription by {subscriber.ToString()}");
            }

            trackingData.subscribers.Add(subscriber);
        }

        public void Unsubscribe(Container self, ref TrackableObjectMixIn<Container> mixIn, Subscriber subscriber)
        {
            // FIXME: This has to happen out-of-line, at the end of the omb.    

            trackingData.subscribers.Remove(subscriber);

            if (trackingData.subscribers.Count == 0)
            {
                Tracker.logger?.Log(GetName(self), "scheduling for relaxation");

                Tracker.ScheduleForRelaxation(self, accessor.GetIndex());
            }
        }

        public void Notify(Container self, ref TrackableObjectMixIn<Container> mixIn, TrackingNotification notification)
        {
            if (trackingData == null) throw new Exception($"Expected {nameof(trackingData)} to be non-null.");

            switch (notification)
            {
                case TrackingNotification.Stale:
                    if (trackingData.staleDependencies++ == 0)
                    {
                        EnsureKnownAsStale(self);
                    }
                    break;
                case TrackingNotification.ReadyUnmodified:
                    if (--trackingData.staleDependencies == 0)
                    {
                        HandleDependenciesStabilized(self);
                        EnsureKnownAsStable(self, false);
                    }
                    break;
                case TrackingNotification.ReadyModified:
                    trackingData.haveModifiedDependencies = true;
                    if (--trackingData.staleDependencies == 0)
                    {
                        HandleDependenciesStabilized(self);
                        EnsureKnownAsStable(self, true);
                    }
                    break;
                case TrackingNotification.RelaxIfAppropriate:
                    RelaxIfAppropriate(self);
                    break;
                default:
                    throw new Exception($"Unknown notification {notification}.");
            }
        }

        void RelaxIfAppropriate(Container self)
        {
            if (trackingData == null) return;

            if (trackingData.subscribers.Count > 0) return;

            Tracker.logger.Log(GetName(self), $"indeed relaxing");

            foreach (var dependency in trackingData.dependencies)
                dependency.target.Unsubscribe(dependency.index, GetOurselvesAsSubscriber(self));

            trackingData = null;
        }

        void HandleDependenciesStabilized(Container self)
        {
            // We shouldn't update yet - only at reaction time.

            if (trackingData.haveModifiedDependencies)
            {
                Tracker.logger?.Log(GetName(self), "having modified dependencies after stabilization");

                var index = accessor.GetIndex();

                Tracker.ScheduleOrRunReaction(() =>
                {
                    self.Update(index, "on stabilized dependencies");
                });
            }
        }

        void EnsureKnownAsStale(Container self)
        {
            if (!trackingData.isKnownToBeStale)
            {
                trackingData.isKnownToBeStale = true;
                Broadcast(self, TrackingNotification.Stale);
            }
        }

        void EnsureKnownAsStable(Container self, Boolean isModified)
        {
            if (trackingData.isKnownToBeStale)
            {
                trackingData.isKnownToBeStale = false;
                Broadcast(self, isModified
                    ? TrackingNotification.ReadyModified
                    : TrackingNotification.ReadyUnmodified);
            }
        }

        void Broadcast(Container self, TrackingNotification notification)
        {
            Tracker.logger?.Log(GetName(self), $"broadcasting {notification} to subscribers");

            foreach (var subscriber in trackingData.subscribers)
                subscriber.target.Notify(subscriber.index, notification);
        }

        void RectifySubscriptions(Container self, List<Dependency> newDependencies)
        {
            var ourselves = GetOurselvesAsSubscriber(self);

            var removedDependencies =
                trackingData.dependencies.Except(newDependencies);

            var addedDependencies =
                newDependencies.Except(trackingData.dependencies);

            foreach (var dependency in removedDependencies)
                dependency.target.Unsubscribe(dependency.index, ourselves);

            foreach (var dependency in addedDependencies)
                dependency.target.Subscribe(dependency.index, ourselves);

            trackingData.dependencies = newDependencies;

            if (trackingData.dependencies.Count == 0)
            {
                Tracker.logger?.Log(GetName(self), $"no dependencies after update, scheduling myself for relaxation");

                Tracker.ScheduleForRelaxation(self, accessor.GetIndex());
            }
        }

        public void Update(Container self, ref TrackableObjectMixIn<Container> mixIn, String reason)
        {
            Tracker.EvaluationRecord record;

            var previousValue = trackingData.cachedValue;
            var previousException = trackingData.cachedException;

            try
            {
                Tracker.OpenEvaluation(GetName(self), reason);

                trackingData.cachedValue = accessor.Get(self);

                trackingData.cachedException = null;

                Tracker.logger?.Log(GetName(self), $"evaluation completed with value '{trackingData.cachedValue}'");
            }
            catch (Exception ex)
            {
                trackingData.cachedException = ex;

                Tracker.logger?.Log(GetName(self), $"evaluation completed with exception '{trackingData.cachedException.Message}'");
            }
            finally
            {
                record = Tracker.CloseEvaluation();
            }

            RectifySubscriptions(self, record.dependencies);

            trackingData.haveModifiedDependencies = false;
        }

        String GetName(Container self)
        {
            var selfName = self.ToString();
            var propertyName = accessor.GetPropertyName();
            return propertyName == null ? selfName : $"{selfName}.{propertyName}";
        }

        Boolean IsEqualToCached(T otherValue, Exception otherException)
        {
            return EqualityComparer<T>.Default.Equals(otherValue, trackingData.cachedValue)
                && otherException == null
                && trackingData.cachedException == null;
        }

        public T Get(Container self, ref TrackableObjectMixIn<Container> mixIn)
        {
            if (Tracker.AreEvaluating)
            {
                Tracker.NoteEvaluation(GetOurselvesAsDependency(self));

                if (trackingData == null)
                {
                    trackingData = new TrackingData<T>();
                    trackingData.haveModifiedDependencies = true;
                }
            }

            if (trackingData != null)
            {
                if (NeedsUpdate)
                {
                    using (Tracker.Batch())
                        Update(self, ref mixIn, "on value request during tracking evaluation");
                }

                return GetCachedValue();
            }
            else
            {
                return accessor.Get(self);
            }
        }

        Boolean NeedsUpdate
            => trackingData.haveModifiedDependencies || trackingData.staleDependencies > 0;

        T GetCachedValue()
        {
            if (trackingData.cachedException != null)
                throw trackingData.cachedException;

            return trackingData.cachedValue;
        }

        public void Set(Container self, ref TrackableObjectMixIn<Container> mixIn, T value)
        {
            try
            {
                Tracker.OpenBatch();

                if (accessor.IsVariable())
                {
                    if (trackingData != null && !IsEqualToCached(value, null))
                    {
                        Tracker.logger?.Log(GetName(self), $"new and different value '{value}' set and we're a variable");

                        EnsureKnownAsStale(self);

                        // FIXME: to be optimized
                        accessor.Set(self, value);
                        trackingData.cachedValue = value;
                        EnsureKnownAsStable(self, isModified: true);
                    }
                    else
                    {
                        accessor.Set(self, value);
                    }
                }
                else
                {
                    // We don't care about non-variable settings.
                    accessor.Set(self, value);
                }
            }
            finally
            {
                Tracker.CloseBatch();
            }
        }

        Subscriber GetOurselvesAsSubscriber(Container self)
            => new Subscriber(self, accessor.GetIndex());

        Dependency GetOurselvesAsDependency(Container self)
            => new Dependency(self, accessor.GetIndex());
    }

    [DebuggerDisplay("{name}")]
    class TrackableVariable<T> : ITrackableObject
    {
        TrackingProperty<T, TrackableVariable<T>, Self> property;

        String name;

        TrackableObjectMixIn<TrackableVariable<T>> mixIn;

        T value;

        struct Self : IAccessor<T, TrackableVariable<T>>
        {
            public string GetPropertyName() => null;

            public int GetIndex() => 0;

            public bool IsVariable() => true;

            public T Get(TrackableVariable<T> outer)
                => outer.value;

            public void Set(TrackableVariable<T> outer, T value)
                => outer.value = value;
        }

        public void Notify(int index, TrackingNotification notification)
            => property.Notify(this, ref mixIn, notification);

        public void Update(int index, String reason)
            => property.Update(this, ref mixIn, reason);

        public void Subscribe(int index, Subscriber subscriber)
            => property.Subscribe(this, ref mixIn, subscriber);

        public void Unsubscribe(int index, Subscriber subscriber)
            => property.Unsubscribe(this, ref mixIn, subscriber);

        public T Value {
            [DebuggerStepThrough]
            get => property.Get(this, ref mixIn);
            [DebuggerStepThrough]
            set => property.Set(this, ref mixIn, value);
        }

        public TrackableVariable(String name, T def)
        {
            this.name = name;
            value = def;
        }

        public override string ToString() => name;

        public string GetPropertyName(int index) => null;
    }

    [DebuggerDisplay("{name}")]
    class TrackableEvaluation<T> : ITrackableObject
    {
        TrackingProperty<T, TrackableEvaluation<T>, Self> property;

        String name;

        TrackableObjectMixIn<TrackableEvaluation<T>> mixIn;

        Func<T> evaluation;

        struct Self : IAccessor<T, TrackableEvaluation<T>>
        {
            public string GetPropertyName() => null;

            public int GetIndex() => 0;

            public bool IsVariable() => false;

            public T Get(TrackableEvaluation<T> outer)
                => outer.evaluation();

            public void Set(TrackableEvaluation<T> outer, T value)
                => throw new NotImplementedException();
        }

        public void Notify(int index, TrackingNotification notification)
            => property.Notify(this, ref mixIn, notification);

        public void Update(int index, String reason)
            => property.Update(this, ref mixIn, reason);

        public void Subscribe(int index, Subscriber subscriber)
            => property.Subscribe(this, ref mixIn, subscriber);

        public void Unsubscribe(int index, Subscriber subscriber)
            => property.Unsubscribe(this, ref mixIn, subscriber);

        public T Value {
            get => property.Get(this, ref mixIn);
        }

        public TrackableEvaluation(String name, Func<T> evaluation)
        {
            this.name = name;
            this.evaluation = evaluation;

            property.Get(this, ref mixIn);
        }

        public override string ToString() => name;

        public string GetPropertyName(int index) => null;
    }

    public interface ITrackable<out T>
    {
        T Value { get; }
    }

    public interface IWritableTrackable<T> : ITrackable<T>
    {
        new T Value { get; set; }
    }

    public struct Var<T> : IWritableTrackable<T>
    {
        internal TrackableVariable<T> variable;

        public T Value {
            [DebuggerStepThrough]
            get => variable.Value;
            [DebuggerStepThrough]
            set => variable.Value = value;
        }
    }

    public struct Eval<T> : ITrackable<T>
    {
        internal TrackableEvaluation<T> evaluation;

        public T Value {
            [DebuggerStepThrough]
            get => evaluation.Value;
        }
    }

    public static class Trackable
    {
        public static Var<T> Var<T>(String name, T def)
            => new Var<T>() { variable = new TrackableVariable<T>(name, def) };

        public static Var<T> Var<T>(T def)
            => new Var<T>() { variable = new TrackableVariable<T>(null, def) };

        public static Eval<T> Eval<T>(String name, Func<T> evaluation)
            => new Eval<T>() { evaluation = new TrackableEvaluation<T>(name, evaluation) };

        public static Eval<T> Eval<T>(Func<T> evaluation)
            => new Eval<T>() { evaluation = new TrackableEvaluation<T>(null, evaluation) };


        // FIXME: not quite correct yet, but usable
        public static IDisposable Autorun(String name, Action action)
            => null;

        public static IDisposable Autorun(Action action)
            => Autorun("<unnamed>", action);

        public static IDisposable Batch() => Tracker.Batch();
    }



    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    [WeaveClass(typeof(TrackableObjectMixIn<>), typeof(TrackingProperty<,,>))]
    public class MoldiniumModelAttribute : Attribute
    {
    }

    [MoldiniumModel]
    public class Class1
    {
    }
}
