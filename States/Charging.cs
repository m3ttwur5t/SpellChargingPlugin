﻿using NetScriptFramework.SkyrimSE;
using SpellChargingPlugin.Core;
using SpellChargingPlugin.StateMachine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpellChargingPlugin.States
{
    public class Charging : State<ChargingSpell>
    {
        public Charging(ChargingSpell context) : base(context)
        {
        }

        /// <summary>
        /// Increase magnitude, transition to Released or Idle states if neccessary
        /// </summary>
        /// <param name="elapsedSeconds"></param>
        protected override void OnUpdate(float elapsedSeconds)
        {
            var handState = SpellHelper.GetSpellAndState(_context.Holder.Actor, _context.Slot);
            
            switch (handState?.State)
            {
                case MagicCastingStates.Charging:
                    break;
                case MagicCastingStates.Charged:
                case MagicCastingStates.Concentrating:
                    _context.UpdateCharge(elapsedSeconds);
                    break;
                case MagicCastingStates.Released:
                    TransitionTo(() => new Release(_context));
                    break;
                case MagicCastingStates.None:
                case null:
                default:
                    TransitionTo(() => new Cancel(_context));
                    break;
            }
        }
    }
}
