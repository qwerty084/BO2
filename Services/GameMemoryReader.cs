using System;
using System.ComponentModel;

namespace BO2.Services
{
    public sealed class GameMemoryReader : IDisposable
    {
        private readonly IProcessMemoryAccessor _processMemoryAccessor;

        public GameMemoryReader()
            : this(new WindowsProcessMemoryAccessor())
        {
        }

        internal GameMemoryReader(IProcessMemoryAccessor processMemoryAccessor)
        {
            ArgumentNullException.ThrowIfNull(processMemoryAccessor);

            _processMemoryAccessor = processMemoryAccessor;
        }

        public PlayerStatsReadResult ReadPlayerStats(DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                _processMemoryAccessor.Close();
                return PlayerStatsReadResult.GameNotRunning;
            }

            if (detectedGame.AddressMap is null)
            {
                _processMemoryAccessor.Close();
                string statusText = string.IsNullOrWhiteSpace(detectedGame.UnsupportedReason)
                    ? AppStrings.Format("UnsupportedStatusFormat", detectedGame.DisplayName)
                    : AppStrings.Format("UnsupportedStatusWithReasonFormat", detectedGame.DisplayName, detectedGame.UnsupportedReason);

                return new PlayerStatsReadResult(
                    detectedGame,
                    null,
                    statusText,
                    ConnectionState.Unsupported);
            }

            try
            {
                _processMemoryAccessor.Attach(detectedGame.ProcessId, detectedGame.ProcessName);
                PlayerStatAddressMap addressMap = detectedGame.AddressMap;
                ScoreStatAddresses scores = addressMap.Scores;
                PlayerStats stats = new(
                    ReadInt32(scores.PointsAddress, "points"),
                    ReadInt32(scores.KillsAddress, "kills"),
                    ReadInt32(scores.DownsAddress, "downs"),
                    ReadInt32(scores.RevivesAddress, "revives"),
                    ReadInt32(scores.HeadshotsAddress, "headshots"),
                    ReadCandidateStats(addressMap.DerivedPlayerState, addressMap.Candidates));

                return new PlayerStatsReadResult(
                    detectedGame,
                    stats,
                    AppStrings.Format("ConnectedStatusFormat", detectedGame.DisplayName),
                    ConnectionState.Connected);
            }
            catch (ArgumentException ex)
            {
                HandleReadFailure();
                throw new InvalidOperationException(
                    AppStrings.Format("InvalidDetectedGameProcessMetadataFormat", detectedGame.DisplayName),
                    ex);
            }
            catch (InvalidOperationException)
            {
                HandleReadFailure();
                throw;
            }
            catch (Win32Exception)
            {
                HandleReadFailure();
                throw;
            }
            catch (Exception)
            {
                HandleReadFailure();
                throw;
            }
        }

        public void Dispose()
        {
            _processMemoryAccessor.Dispose();
        }

        private PlayerCandidateStats ReadCandidateStats(
            DerivedPlayerStateAddresses derivedPlayerState,
            PlayerCandidateAddresses candidates)
        {
            return new(
                TryReadSingle(derivedPlayerState.PositionXAddress, "position X"),
                TryReadSingle(derivedPlayerState.PositionYAddress, "position Y"),
                TryReadSingle(derivedPlayerState.PositionZAddress, "position Z"),
                TryReadInt32(candidates.LegacyHealthAddress, "legacy health"),
                TryReadInt32(candidates.PlayerInfoHealthAddress, "player info health"),
                TryReadInt32(candidates.GEntityPlayerHealthAddress, "GEntity player health"),
                TryReadSingle(candidates.VelocityXAddress, "velocity X"),
                TryReadSingle(candidates.VelocityYAddress, "velocity Y"),
                TryReadSingle(candidates.VelocityZAddress, "velocity Z"),
                TryReadInt32(candidates.GravityAddress, "gravity"),
                TryReadInt32(candidates.SpeedAddress, "speed"),
                TryReadSingle(candidates.LastJumpHeightAddress, "last jump height"),
                TryReadSingle(candidates.AdsAmountAddress, "ADS amount"),
                TryReadSingle(candidates.ViewAngleXAddress, "view angle X"),
                TryReadSingle(candidates.ViewAngleYAddress, "view angle Y"),
                TryReadInt32(candidates.HeightIntAddress, "height integer"),
                TryReadSingle(candidates.HeightFloatAddress, "height float"),
                TryReadInt32(candidates.AmmoSlot0Address, "ammo slot 0"),
                TryReadInt32(candidates.AmmoSlot1Address, "ammo slot 1"),
                TryReadInt32(candidates.LethalAmmoAddress, "lethal ammo"),
                TryReadInt32(candidates.AmmoSlot2Address, "ammo slot 2"),
                TryReadInt32(candidates.TacticalAmmoAddress, "tactical ammo"),
                TryReadInt32(candidates.AmmoSlot3Address, "ammo slot 3"),
                TryReadInt32(candidates.AmmoSlot4Address, "ammo slot 4"),
                TryReadInt32(candidates.AlternateKillsAddress, "alternate kills"),
                TryReadInt32(candidates.AlternateHeadshotsAddress, "alternate headshots"),
                TryReadInt32(candidates.SecondaryKillsAddress, "secondary kills"),
                TryReadInt32(candidates.SecondaryHeadshotsAddress, "secondary headshots"),
                TryReadInt32(candidates.RoundAddress, "round"));
        }

        private int? TryReadInt32(uint address, string valueName)
        {
            try
            {
                return ReadInt32(address, valueName);
            }
            catch (Win32Exception)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private float? TryReadSingle(uint address, string valueName)
        {
            try
            {
                return ReadSingle(address, valueName);
            }
            catch (Win32Exception)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private int ReadInt32(uint address, string valueName)
        {
            return _processMemoryAccessor.ReadInt32(address, valueName);
        }

        private float ReadSingle(uint address, string valueName)
        {
            return _processMemoryAccessor.ReadSingle(address, valueName);
        }

        private void HandleReadFailure()
        {
            _processMemoryAccessor.Close();
        }
    }
}
