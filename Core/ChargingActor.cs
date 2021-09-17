﻿using NetScriptFramework;
using NetScriptFramework.SkyrimSE;
using NetScriptFramework.Tools;
using SpellChargingPlugin.StateMachine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpellChargingPlugin.Core
{
    public class ChargingActor
    {
        public enum OperationMode { Disabled, Magnitude, Duration }
        private Dictionary<OperationMode, uint> _modeArtObjects = new Dictionary<OperationMode, uint>
        {
            { OperationMode.Disabled, 0x0 },
            { OperationMode.Magnitude, Settings.Instance.ArtObjectMagnitude },
            { OperationMode.Duration, Settings.Instance.ArtObjectDuration },
        };
        public Character Actor { get; }
        public OperationMode Mode { get; private set; } = OperationMode.Disabled;
        public bool IsHoldingKey => _hotKeyPress?.IsPressed() == true;

        private ChargingSpell _chargingSpellLeft = null;
        private ChargingSpell _chargingSpellRight = null;
        private MaintainedSpell _maintainedSpell = null;

        private bool _leftEqualsRight = false;
        private HotkeyPress _hotKeyPress;

        public ChargingActor(Character character)
        {
            Actor = character;
            Register();

            if (!Enum.TryParse<OperationMode>(Settings.Instance.OperationMode, out var mode))
                mode = OperationMode.Magnitude;
            SetOperationMode(mode);

            _maintainedSpell = MaintainedSpell.TryRestore(this);
        }

        /// <summary>
        /// Allies will cast the spell
        /// </summary>
        /// <param name="spell"></param>
        /// <param name="range"></param>
        internal void ShareSpell(SpellItem spell, float range)
        {
            var inRange = Util.GetCharactersInRange(Actor, range);
            // TODO: summoned creatures do not count as "player teammate", but probably should (or not? too strong otherwise?)
            foreach (var ally in inRange.Where(chr => !chr.IsDead && chr.IsPlayerTeammate && !chr.IsPlayer))
                ally.CastSpell(spell, ally, Actor);
            MenuManager.ShowHUDMessage($"Share : {spell.Name}", null, false);
        }

        internal void MaintainSpell(ChargingSpell chargingSpell)
        {
            if (_maintainedSpell != null)
                _maintainedSpell.Dispel();
            var newMSpell = new MaintainedSpell(this, chargingSpell.Spell);
            newMSpell.Apply(chargingSpell.ChargeLevel);
            _maintainedSpell = newMSpell;
        }

        /// <summary>
        /// Remove sticky ArtObject
        /// </summary>
        public void CleanArtObj()
        {
            foreach (var fid in _modeArtObjects)
                Util.Visuals.DetachArtObject(fid.Value, Actor);
        }

        /// <summary>
        /// Do all event registrations here
        /// </summary>
        private void Register()
        {
            if (Actor.BaseForm.FormId != PlayerCharacter.Instance.BaseForm.FormId)
                return;

            if (!HotkeyBase.TryParse(Settings.Instance.HotKey, out var keys))
                keys = new VirtualKeys[] { VirtualKeys.Shift, VirtualKeys.G };
            _hotKeyPress = new HotkeyPress(() => HandleContextAwareKey(), keys);
            _hotKeyPress.Register();

            // Having something like this would be nice
            //Events.OnEquipWeaponOrSpell.Register(arg => { });
        }

        private void HandleContextAwareKey()
        {
            // dispel Maintain when hands down and no spells equipped
            if(!Actor.IsWeaponDrawn && _chargingSpellLeft == null && _chargingSpellRight == null && _maintainedSpell != null)
            {
                _maintainedSpell.Dispel();
                return;
            }

            // use hotkey for Maintain & Share once charging/casting has begun
            if (_chargingSpellLeft != null && !(_chargingSpellLeft.CurrentState is StateMachine.States.Idle))
                return;
            if (!_leftEqualsRight && _chargingSpellRight != null && !(_chargingSpellRight.CurrentState is StateMachine.States.Idle))
                return;

            // swutch between Magnitude and Duration mode otherwise
            if (Mode != OperationMode.Magnitude)
                SetOperationMode(OperationMode.Magnitude);
            else
                SetOperationMode(OperationMode.Duration);
        }

        public void RefreshSpellParticleNodes()
        {
            _chargingSpellLeft?.RefreshParticleNode();
            _chargingSpellRight?.RefreshParticleNode();
        }

        /// <summary>
        /// Switch between modes or disable altogether
        /// </summary>
        private void SetOperationMode(OperationMode newMode)
        {
            CleanArtObj();

            MenuManager.ShowHUDMessage($"Overcharge Priority : {newMode}", null, false);

            Util.Visuals.AttachArtObject(_modeArtObjects[newMode], Actor, 2f);
            if (newMode == OperationMode.Disabled)
            {
                ClearSpell(ref _chargingSpellLeft);
                ClearSpell(ref _chargingSpellRight);
            }

            Mode = newMode;
        }

        /// <summary>
        /// Check for changes in equipped spells and refresh their states
        /// </summary>
        /// <param name="elapsedSeconds"></param>
        public void Update(float elapsedSeconds)
        {
            if (_maintainedSpell != null)
            {
                _maintainedSpell.Update(elapsedSeconds);
                if (_maintainedSpell.Dispelled)
                    _maintainedSpell = null;
            }

            if (Mode == OperationMode.Disabled)
                return;
            AssignSpellsIfNecessary();
            _chargingSpellLeft?.Update(elapsedSeconds);
            if (!_leftEqualsRight)
                _chargingSpellRight?.Update(elapsedSeconds);
        }

        public bool TryDrainMagicka(float magCost)
        {
            if (Actor.GetActorValue(ActorValueIndices.Magicka) < magCost)
                return false;
            Actor.DamageActorValue(ActorValueIndices.Magicka, -magCost);
            return true;
        }

        /// <summary>
        /// Check if the character has spells equipped and assign them to their appropriate <cref>ChargingSpell</cref> slots.
        /// Also take care of cleaning up the previous <cref>ChargingSpell</cref> if overwriting.
        /// </summary>
        private void AssignSpellsIfNecessary()
        {
            var actualLeftSpell = SpellHelper.GetSpell(Actor, EquippedSpellSlots.LeftHand);

            if (actualLeftSpell == null)
            {
                ClearSpell(ref _chargingSpellLeft);
                _leftEqualsRight = false;
            }
            else if (_chargingSpellLeft == null || _chargingSpellLeft.Spell.FormId != actualLeftSpell.FormId)
            {
                ClearSpell(ref _chargingSpellLeft);
                _chargingSpellLeft = SetSpell(actualLeftSpell, EquippedSpellSlots.LeftHand);
                _leftEqualsRight = _chargingSpellLeft.IsTwoHanded == true;
            }

            // Can skip right hand check and assignment when using master-tier or other two-handed spells.
            if (!_leftEqualsRight)
            {
                var actualRightSpell = SpellHelper.GetSpell(Actor, EquippedSpellSlots.RightHand);
                if (actualRightSpell == null)
                {
                    ClearSpell(ref _chargingSpellRight);
                }
                else if (_chargingSpellRight == null || _chargingSpellRight.Spell.FormId != actualRightSpell.FormId)
                {
                    ClearSpell(ref _chargingSpellRight);
                    _chargingSpellRight = SetSpell(actualRightSpell, EquippedSpellSlots.RightHand);
                }
            }
        }

        /// <summary>
        /// Reset the charging spell null the reference
        /// </summary>
        /// <param name="spell"></param>
        private void ClearSpell(ref ChargingSpell spell)
        {
            spell?.Clean();
            spell = null;
        }

        /// <summary>
        /// Create a new charging spell
        /// </summary>
        /// <param name="spellItem"></param>
        /// <param name="handSlot"></param>
        /// <returns></returns>
        private ChargingSpell SetSpell(SpellItem spellItem, EquippedSpellSlots handSlot)
        {
            DebugHelper.Print($"[ChargingActor] Hand: {handSlot} -> {spellItem?.Name}");
            return new ChargingSpell(this, spellItem, handSlot);
        }



        /// <summary>
        /// Check if the character is dual casting (same spell in both hands, dual casting perk, casting both)
        /// </summary>
        /// <returns></returns>
        public bool IsDualCasting()
        {
            return Memory.InvokeThisCall(Actor.Cast<Character>(), Util.addr_ActorIsDualCasting).ToBool();
        }
    }
}
