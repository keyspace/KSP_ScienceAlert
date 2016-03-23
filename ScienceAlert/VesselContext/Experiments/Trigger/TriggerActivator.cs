﻿using System;
using System.Collections.Generic;
using System.Linq;
using ReeperCommon.Logging;

namespace ScienceAlert.VesselContext.Experiments.Trigger
{
// ReSharper disable once ClassNeverInstantiated.Global
    public class TriggerActivator
    {
        private readonly SignalDeployExperimentFinished _finishedSignal;
        private readonly ExperimentTrigger[] _triggers;

        private bool _waitingOnTrigger = false;

        public TriggerActivator(IEnumerable<ExperimentTrigger> triggers, SignalDeployExperimentFinished finishedSignal)
        {
            if (triggers == null) throw new ArgumentNullException("triggers");
            if (finishedSignal == null) throw new ArgumentNullException("finishedSignal");

            _triggers = triggers.ToArray();
            _finishedSignal = finishedSignal;

            var duplicates = _triggers
                .Select(t => new KeyValuePair<ExperimentTrigger, string>(t, t.Experiment.id))
                .GroupBy(kvp => kvp.Value)
                .Where(grouping => grouping.Count() > 1)
                .ToList();

            if (duplicates.Count <= 0) return;

            duplicates.Select(grouping => grouping.Key)
                .ToList()
                .ForEach(
                    duplicateTriggerExperimentId =>
                        Log.Warning("Duplicate ExperimentTrigger for " + duplicateTriggerExperimentId));

            throw new ArgumentException("Cannot have multiple triggers for one experiment");
        }


        public void ActivateTriggerFor(ScienceExperiment experiment)
        {
            if (experiment == null) throw new ArgumentNullException("experiment");

            try
            {
                var trigger = _triggers.Single(t => t.Experiment.id == experiment.id);

                DeployTrigger(trigger);
            }
            catch (InvalidOperationException)
            {
                throw new ArgumentException("No trigger found for " + experiment.id);
            }
        }


        private void DeployTrigger(ExperimentTrigger trigger)
        {
            if (trigger == null) throw new ArgumentNullException("trigger");

            if (trigger.Busy)
            {
                Log.Warning("Trigger for " + trigger.Experiment.id + " is busy");
                return;
            }

            if (_waitingOnTrigger)
            {
                Log.Warning("Waiting on a previous trigger to complete");
                return;
            }

            try
            {
                trigger.Deploy()
                    .Then(() => FinishedDeploying(trigger.Experiment))
                    .Fail(e => DeployFailed(trigger.Experiment, e))
                    .Finally(StopWaiting);
                _waitingOnTrigger = true;
            }
            catch (Exception)
            {
                Log.Error("Failed to deploy trigger for " + trigger.Experiment.id);
                throw;
            }
        }


        private void FinishedDeploying(ScienceExperiment experiment)
        {
            _finishedSignal.Dispatch(experiment, true);
        }


        private void DeployFailed(ScienceExperiment experiment, Exception why)
        {
            _finishedSignal.Dispatch(experiment, false);
            Log.Error("Deploy failed: " + why);
        }


        private void StopWaiting()
        {
            _waitingOnTrigger = false;
        }
    }
}