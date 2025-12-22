﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Numerics;

namespace TeleportAnglesFix
{
    public class TeleportAnglesFix : BasePlugin
    {
        public override string ModuleName { get; } = "TeleportAnglesFix";
        public override string ModuleVersion { get; } = "1.3";
        public override string ModuleAuthor { get; } = "Retro (updated by Marchand)";

        private Dictionary<int, QAngle> _angleCache = new();
        private string _currentMapName = string.Empty;
        private HashSet<string> _targetMaps = new HashSet<string>();

        private class Config
        {
            public List<string> TargetMaps { get; set; } = new List<string>();
        }

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            _currentMapName = Server.MapName;
            LoadConfig();

            RegisterEventHandler<EventPlayerDisconnect>((@event, info) =>
            {
                var player = @event.Userid;
                if (player is { IsValid: true } && !player.IsBot)
                    _angleCache.Remove(player.Slot);

                return HookResult.Continue;
            });

            RegisterListener<Listeners.OnMapStart>(OnMapStart);
        }

        public override void Unload(bool hotReload)
        {
            _angleCache.Clear();
            RemoveListener<Listeners.OnMapStart>(OnMapStart);

        }
        private void OnMapStart(string mapName)
        {
            _currentMapName = mapName;
            _angleCache.Clear();
        }

        private void LoadConfig()
        {
            try
            {
                var configPath = Path.Combine(ModuleDirectory, "maplist.json");
                if (!File.Exists(configPath))
                    CreateDefaultConfig(configPath);

                var configJson = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Config>(configJson);
                if (config != null)
                    _targetMaps = new HashSet<string>(config.TargetMaps ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                else
                    Logger.LogError("Failed to load configuration: deserialization returned null");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "An error occurred while loading the configuration");
            }
        }

        private void CreateDefaultConfig(string configPath)
        {
            var defaultConfig = new Config
            {
                TargetMaps = new List<string> { "surf_reprise" }
            };

            var configJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, configJson);
            Logger.LogInformation("|---| Default configuration file created at: " + configPath);
        }

        [EntityOutputHook("trigger_teleport", "OnStartTouch")]
        public HookResult OnStartTouch(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
        {
            if (activator.DesignerName != "player" || !_targetMaps.Contains(_currentMapName))
                return HookResult.Continue;

            var pawn = activator.As<CCSPlayerPawn>();
            if (!pawn.IsValid || !pawn.Controller.IsValid || pawn.Controller.Value is null)
                return HookResult.Continue;

            var controller = pawn.Controller.Value.As<CCSPlayerController>();
            if (controller.SteamID <= 0)
                return HookResult.Continue;

            var teleport = caller.As<CTriggerTeleport>();
            
            if (string.IsNullOrEmpty(teleport.Landmark))
                return HookResult.Continue;

            Vector3 eyeVec = pawn.EyeAngles.ToVector3();
            _angleCache[controller.Slot] = eyeVec.ToQAngle();

            return HookResult.Continue;
        }

        [EntityOutputHook("trigger_teleport", "OnEndTouch")]
        public HookResult OnEndTouch(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
        {
            if (activator.DesignerName != "player" || !_targetMaps.Contains(_currentMapName)) return HookResult.Continue;

            var pawn = new CCSPlayerPawn(activator.Handle);
            if (!pawn.IsValid || !pawn.Controller.IsValid || pawn.Controller.Value is null)
                return HookResult.Continue;

            var controller = pawn.Controller.Value.As<CCSPlayerController>();
            if (controller.SteamID <= 0)
                return HookResult.Continue;

            var teleport = caller.As<CTriggerTeleport>();
            
            if (string.IsNullOrEmpty(teleport.Landmark))
                return HookResult.Continue;

            if (!_angleCache.TryGetValue(controller.Slot, out var angle))
                return HookResult.Continue;

            Server.RunOnTick(Server.TickCount + 1, () =>
            {
                if (pawn.IsValid && pawn.Controller.IsValid && pawn.Controller.Value is not null)
                {
                    pawn.Teleport(angles: angle);
                }
                _angleCache.Remove(controller.Slot);
            });

            return HookResult.Continue;
        }
    }

    public static class QAngleNumericsExtensions
    {
        public static Vector3 ToVector3(this QAngle a) => new Vector3(a.X, a.Y, a.Z);
        public static QAngle ToQAngle(this Vector3 v) => new QAngle(v.X, v.Y, v.Z);
    }
}