﻿using NetScriptFramework.SkyrimSE;
using SpellChargingPlugin.Core;
using SpellChargingPlugin.ParticleSystem.Behaviors;
using SpellChargingPlugin.StateMachine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpellChargingPlugin.StateMachine.States
{
    public class Idle : State<ChargingSpell>
    {
        private Util.SimpleTimer _preChargeControlTimer = new Util.SimpleTimer();
        private Util.SimpleTimer _stateResetControlTimer = new Util.SimpleTimer();
        private bool _needsReset = true;
        public Idle(ChargingSpell context) : base(context)
        {
        }

        protected override void OnUpdate(float elapsedSeconds)
        {
            var handState = SpellHelper.GetSpellAndState(_context.Holder.Actor, _context.Slot);
            if (handState == null)
                return;

            _stateResetControlTimer.Update(elapsedSeconds);
            if (_stateResetControlTimer.HasElapsed(Settings.Instance.AutoCleanupDelay, out _))
            {
                DebugHelper.Print($"[State.Idle:{_context.Spell.Name}] Auto cleanup.");
                _context.ResetAndClean();
                _stateResetControlTimer.Enabled = false;
                _needsReset = false;
            }

            switch (handState.Value.State)
            {
                case MagicCastingStates.Concentrating:
                    if (!Settings.Instance.AllowConcentrationSpells)
                        break;
                    goto case MagicCastingStates.Charged;
                case MagicCastingStates.Charged:
                    if (_needsReset)
                    {
                        _context.ResetAndClean();
                        _needsReset = false;
                    }
                    _preChargeControlTimer.Update(elapsedSeconds);
                    if (!_preChargeControlTimer.HasElapsed(Settings.Instance.PreChargeDelay, out _))
                        return;
                    TransitionTo(() => new Charging(_context));
                    break;
            }
        }
    }
}
