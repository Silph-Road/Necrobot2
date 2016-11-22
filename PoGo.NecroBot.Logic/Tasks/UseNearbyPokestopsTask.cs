﻿#region using directives

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeoCoordinatePortable;
using PoGo.NecroBot.Logic.Common;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Strategies.Walk;
using PoGo.NecroBot.Logic.Utils;
using PokemonGo.RocketAPI.Extensions;
using POGOProtos.Map.Fort;
using POGOProtos.Networking.Responses;
using PoGo.NecroBot.Logic.Event.Gym;
using PoGo.NecroBot.Logic.Model;
using PoGo.NecroBot.Logic.Exceptions;
using PoGo.NecroBot.Logic.Model.Settings;

#endregion

namespace PoGo.NecroBot.Logic.Tasks
{
    public class UseNearbyPokestopsTask
    {
        private static int _stopsHit;
        private static int _randomStop;
        private static Random _rc; //initialize pokestop random cleanup counter first time
        private static int _storeRi;
        private static int _randomNumber;
        public static bool _pokestopLimitReached;
        public static bool _pokestopTimerReached;

        internal static void Initialize()
        {
            _stopsHit = 0;
            _randomStop = 0;
            _rc = new Random();
            _storeRi = _rc.Next(8, 15);
            _randomNumber = _rc.Next(4, 11);
            _pokestopLimitReached = false;
            _pokestopTimerReached = false;
        }

        private static bool SearchThresholdExceeds(ISession session)
        {
            if (!session.LogicSettings.UsePokeStopLimit) return false;
            if (_pokestopLimitReached || _pokestopTimerReached) return true;

            session.Stats.CleanOutExpiredStats();

            // Check if user defined max Pokestops reached
            var timeDiff = (DateTime.Now - session.Stats.StartTime);

            if (session.Stats.GetNumPokestopsInLast24Hours() >= session.LogicSettings.PokeStopLimit)
            {
                session.EventDispatcher.Send(new ErrorEvent
                {
                    Message = session.Translation.GetTranslation(TranslationString.PokestopLimitReached)
                });
                
                _pokestopLimitReached = true;
                return true;
            }

            // Check if user defined time since start reached
            else if (timeDiff.TotalSeconds >= session.LogicSettings.PokeStopLimitMinutes * 60)
            {
                session.EventDispatcher.Send(new ErrorEvent
                {
                    Message = session.Translation.GetTranslation(TranslationString.PokestopTimerReached)
                });
                
                _pokestopTimerReached = true;
                return true;
            }

            return false; // Continue running
        }

        public static async Task Execute(ISession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //request map objects to referesh data. keep all fort in session

            var mapObjectTupe = await GetPokeStops(session);
            var pokeStop = await GetNextPokeStop(session);

            while (pokeStop != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Exit this task if both catching and looting has reached its limits
                if ((UseNearbyPokestopsTask._pokestopLimitReached || UseNearbyPokestopsTask._pokestopTimerReached) &&
                    (CatchPokemonTask._catchPokemonLimitReached || CatchPokemonTask._catchPokemonTimerReached))
                    return;
                var fortInfo = pokeStop.Id == SetMoveToTargetTask.TARGET_ID ? SetMoveToTargetTask.FortInfo : await session.Client.Fort.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                await WalkingToPokeStop(session, cancellationToken, pokeStop, fortInfo);

                await DoActionAtPokeStop(session, cancellationToken, pokeStop, fortInfo);

                await UseGymBattleTask.Execute(session, cancellationToken, pokeStop, fortInfo);

                if (session.LogicSettings.SnipeAtPokestops || session.LogicSettings.UseSnipeLocationServer)
                    await SnipePokemonTask.Execute(session, cancellationToken);

                if (!await SetMoveToTargetTask.IsReachedDestination(pokeStop, session, cancellationToken))
                {
                    pokeStop.CooldownCompleteTimestampMs = DateTime.UtcNow.ToUnixTime() + (pokeStop.Type == FortType.Gym ? session.LogicSettings.GymVisitTimeout : 5) * 60 * 1000; //5 minutes to cooldown
                    session.AddForts(new List<FortData>() { pokeStop }); //replace object in memory.
                }

                if (session.LogicSettings.EnableHumanWalkingSnipe)
                {
                    await HumanWalkSnipeTask.Execute(session, cancellationToken, pokeStop, fortInfo);
                }
                pokeStop = await GetNextPokeStop(session);
            }
        }

        private static async Task WalkingToPokeStop(ISession session, CancellationToken cancellationToken, FortData pokeStop, FortDetailsResponse fortInfo)
        {
            var distance = LocationUtils.CalculateDistanceInMeters(session.Client.CurrentLatitude,
                    session.Client.CurrentLongitude, pokeStop.Latitude, pokeStop.Longitude);

            // we only move to the PokeStop, and send the associated FortTargetEvent, when not using GPX
            // also, GPX pathing uses its own EggWalker and calls the CatchPokemon tasks internally.
            if (!session.LogicSettings.UseGpxPathing)
            {
                var eggWalker = new EggWalker(1000, session);

                cancellationToken.ThrowIfCancellationRequested();

                // Always set the fort info in base walk strategy.

                var pokeStopDestination = new FortLocation(pokeStop.Latitude, pokeStop.Longitude,
                    LocationUtils.getElevation(session.ElevationService, pokeStop.Latitude, pokeStop.Longitude), pokeStop, fortInfo);

                await session.Navigation.Move(pokeStopDestination,
                 async () =>
                 {
                     await MSniperServiceTask.Execute(session, cancellationToken);

                     await OnWalkingToPokeStopOrGym(session, pokeStop, cancellationToken);
                 },
                             session,
                             cancellationToken);

                // we have moved this distance, so apply it immediately to the egg walker.
                await eggWalker.ApplyDistance(distance, cancellationToken);
            }
        }
        private static async Task OnWalkingToPokeStopOrGym(ISession session, FortData pokeStop, CancellationToken cancellationToken)
        {
            // Catch normal map Pokemon
            await CatchNearbyPokemonsTask.Execute(session, cancellationToken);
            //Catch Incense Pokemon
            await CatchIncensePokemonsTask.Execute(session, cancellationToken);

            if (!session.LogicSettings.UseGpxPathing)
            {
                // Spin as long as we haven't reached the user defined limits
                if (!_pokestopLimitReached && !_pokestopTimerReached)
                {
                    await SpinPokestopNearBy(session, cancellationToken, pokeStop);
                }
            }
        }
        public static async Task<FortData> GetNextPokeStop(ISession session)
        {

            var priorityTarget = await SetMoveToTargetTask.GetTarget(session);
            if (priorityTarget != null) return priorityTarget;


            if (session.Forts == null ||
                session.Forts.Count == 0 ||
                session.Forts.Count(p => p.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime()) == 0)
            {
                //TODO : A logic need to be add for handle this  case?
            };

            var forts = session.Forts.Where(p => p.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime()).ToList();
            forts = forts.OrderBy(
                        p =>
                            session.Navigation.WalkStrategy.CalculateDistance(
                                session.Client.CurrentLatitude,
                                session.Client.CurrentLongitude,
                                p.Latitude,
                                p.Longitude,
                                session)
                                ).ToList();

            if (session.LogicSettings.UseGpxPathing)
            {
                forts = forts.Where(p => LocationUtils.CalculateDistanceInMeters(p.Latitude, p.Longitude, session.Client.CurrentLatitude, session.Client.CurrentLongitude) < 40).ToList();
            }

            if (!session.LogicSettings.GymAllowed /*|| session.Inventory.GetPlayerStats().Result.FirstOrDefault().Level <= 5*/)
            {
                // Filter out the gyms
                forts = forts.Where(x => x.Type != FortType.Gym).ToList();
            }
            else if (session.LogicSettings.GymPrioritizeOverPokestop)
            {
                // Prioritize gyms over pokestops
                var gyms = forts.Where(x => x.Type == FortType.Gym &&
                    LocationUtils.CalculateDistanceInMeters(x.Latitude, x.Longitude, session.Client.CurrentLatitude, session.Client.CurrentLongitude) < session.LogicSettings.GymMaxDistance);

                // Return the first gym in range.
                if (gyms.Count() > 0)
                    return gyms.FirstOrDefault();
            }

            if (forts.Count == 1)
                return forts.FirstOrDefault();

            return forts.Skip((int)DateTime.Now.Ticks % 2).FirstOrDefault();
        }

        public static async Task SpinPokestopNearBy(ISession session, CancellationToken cancellationToken, FortData destinationFort = null)
        {
            var allForts = session.Forts.Where(p => p.Type == FortType.Checkpoint).ToList();

            if (allForts.Count > 1)
            {
                var spinablePokestops = allForts.Where(
                    i =>
                        (
                            LocationUtils.CalculateDistanceInMeters(
                                session.Client.CurrentLatitude, session.Client.CurrentLongitude,
                                i.Latitude, i.Longitude) < 40 &&
                                i.CooldownCompleteTimestampMs == 0 &&
                                (destinationFort == null || destinationFort.Id != i.Id))
                ).ToList();

                List<FortData> spinedPokeStops = new List<FortData>();
                if (spinablePokestops.Count >= 1)
                {
                    foreach (var pokeStop in spinablePokestops)
                    {
                        var fortInfo = await session.Client.Fort.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                        await FarmPokestop(session, pokeStop, fortInfo, cancellationToken, true);
                        pokeStop.CooldownCompleteTimestampMs = DateTime.UtcNow.ToUnixTime() + 5 * 60 * 1000;
                        spinedPokeStops.Add(pokeStop);
                        if (spinablePokestops.Count > 1)
                        {
                            await Task.Delay(1000);
                        }
                    }
                }
                session.AddForts(spinablePokestops);
            }
        }

        private static async Task DoActionAtPokeStop(ISession session, CancellationToken cancellationToken, FortData pokeStop, FortDetailsResponse fortInfo, bool doNotTrySpin = false)
        {
            if (pokeStop.Type != FortType.Checkpoint) return;

            //Catch Lure Pokemon
            if (pokeStop.LureInfo != null)
            {
                // added for cooldowns
                await Task.Delay(Math.Min(session.LogicSettings.DelayBetweenPlayerActions, 3000));
                await CatchLurePokemonsTask.Execute(session, pokeStop, cancellationToken);
            }

            // Spin as long as we haven't reached the user defined limits
            if (!_pokestopLimitReached && !_pokestopTimerReached)
            {
                await FarmPokestop(session, pokeStop, fortInfo, cancellationToken, doNotTrySpin);
            }
            else
            {
                // We hit the pokestop limit but not the pokemon limit. So we want to set the cooldown on the pokestop so that
                // we keep moving and don't walk back and forth between 2 pokestops.
                pokeStop.CooldownCompleteTimestampMs = DateTime.UtcNow.ToUnixTime() + 5 * 60 * 1000; // 5 minutes to cooldown for pokestop.
            }

            if (++_stopsHit >= _storeRi) //TODO: OR item/pokemon bag is full //check stopsHit against storeRI random without dividing.
            {
                _storeRi = _rc.Next(6, 12); //set new storeRI for new random value
                _stopsHit = 0;

                if (session.LogicSettings.UseNearActionRandom)
                {
                    await HumanRandomActionTask.Execute(session, cancellationToken);
                }
                else
                {
                    await RecycleItemsTask.Execute(session, cancellationToken);

                    if (session.LogicSettings.EvolveAllPokemonWithEnoughCandy ||
                        session.LogicSettings.EvolveAllPokemonAboveIv ||
                        session.LogicSettings.UseLuckyEggsWhileEvolving ||
                        session.LogicSettings.KeepPokemonsThatCanEvolve)
                        await EvolvePokemonTask.Execute(session, cancellationToken);
                    if (session.LogicSettings.UseLuckyEggConstantly)
                        await UseLuckyEggConstantlyTask.Execute(session, cancellationToken);
                    if (session.LogicSettings.UseIncenseConstantly)
                        await UseIncenseConstantlyTask.Execute(session, cancellationToken);
                    if (session.LogicSettings.TransferDuplicatePokemon)
                        await TransferDuplicatePokemonTask.Execute(session, cancellationToken);
                    if (session.LogicSettings.TransferWeakPokemon)
                        await TransferWeakPokemonTask.Execute(session, cancellationToken);
                    if (session.LogicSettings.RenamePokemon)
                        await RenamePokemonTask.Execute(session, cancellationToken);
                    if (session.LogicSettings.AutomaticallyLevelUpPokemon)
                        await LevelUpPokemonTask.Execute(session, cancellationToken);

                    GetPokeDexCount.Execute(session, cancellationToken);
                }
            }
        }

        private static int softbanCount = 0;

        private static async Task FarmPokestop(ISession session, FortData pokeStop, FortDetailsResponse fortInfo, CancellationToken cancellationToken, bool doNotRetry = false)
        {
            // If the cooldown is in the future than don't farm the pokestop.
            if (pokeStop.CooldownCompleteTimestampMs > DateTime.UtcNow.ToUnixTime())
                return;

            FortSearchResponse fortSearch;
            var timesZeroXPawarded = 0;
            var fortTry = 0; //Current check
            const int retryNumber = 50; //How many times it needs to check to clear softban
            const int zeroCheck = 5; //How many times it checks fort before it thinks it's softban
            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (SearchThresholdExceeds(session))
                {
                    break;
                }

                fortSearch =
                    await session.Client.Fort.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                if (fortSearch.ExperienceAwarded > 0 && timesZeroXPawarded > 0) timesZeroXPawarded = 0;
                if (fortSearch.ExperienceAwarded == 0 && fortSearch.Result != FortSearchResponse.Types.Result.InventoryFull)
                {
                    timesZeroXPawarded++;

                    if (timesZeroXPawarded > zeroCheck)
                    {
                        if ((int)fortSearch.CooldownCompleteTimestampMs != 0)
                        {
                            break; // Check if successfully looted, if so program can continue as this was "false alarm".
                        }

                        fortTry += 1;

                        session.EventDispatcher.Send(new FortFailedEvent
                        {
                            Name = fortInfo.Name,
                            Try = fortTry,
                            Max = retryNumber - zeroCheck,
                            Looted = false
                        });
                        if (doNotRetry)
                        {
                            break;
                        }
                        if (!session.LogicSettings.FastSoftBanBypass)
                        {
                            DelayingUtils.Delay(session.LogicSettings.DelayBetweenPlayerActions, 0);
                        }
                    }
                }
                else
                {
                    if (fortTry != 0)
                    {
                        session.EventDispatcher.Send(new FortFailedEvent
                        {
                            Name = fortInfo.Name,
                            Try = fortTry + 1,
                            Max = retryNumber - zeroCheck,
                            Looted = true
                        });
                    }

                    session.EventDispatcher.Send(new FortUsedEvent
                    {
                        Id = pokeStop.Id,
                        Name = fortInfo.Name,
                        Exp = fortSearch.ExperienceAwarded,
                        Gems = fortSearch.GemsAwarded,
                        Items = StringUtils.GetSummedFriendlyNameOfItemAwardList(fortSearch.ItemsAwarded),
                        Latitude = pokeStop.Latitude,
                        Longitude = pokeStop.Longitude,
                        Altitude = session.Client.CurrentAltitude,
                        InventoryFull = fortSearch.Result == FortSearchResponse.Types.Result.InventoryFull
                    });
                    if (fortSearch.Result == FortSearchResponse.Types.Result.InventoryFull)
                    {
                        await RecycleItemsTask.Execute(session, cancellationToken);
                        _storeRi = 1;
                    }

                    if (session.LogicSettings.UsePokeStopLimit)
                    {
                        session.Stats.AddPokestopTimestamp(DateTime.Now.Ticks);
                        Logger.Write($"(POKESTOP LIMIT) {session.Stats.GetNumPokestopsInLast24Hours()}/{session.LogicSettings.PokeStopLimit}",
                            LogLevel.Info, ConsoleColor.Yellow);
                    }
                    break; //Continue with program as loot was succesfull.
                }
            } while (fortTry < retryNumber - zeroCheck);
            //Stop trying if softban is cleaned earlier or if 40 times fort looting failed.

            if (MultipleBotConfig.IsMultiBotActive(session.LogicSettings))
            {
                if (fortTry >= retryNumber - zeroCheck)
                {
                    softbanCount++;

                    //only check if PokestopSoftbanCount > 0
                    if (MultipleBotConfig.IsMultiBotActive(session.LogicSettings) &&
                        session.LogicSettings.MultipleBotConfig.PokestopSoftbanCount > 0 &&
                        session.LogicSettings.MultipleBotConfig.PokestopSoftbanCount <= softbanCount)
                    {
                        softbanCount = 0;
                        session.CancellationTokenSource.Cancel();

                        //Activate switcher by pokestop
                        throw new ActiveSwitchByRuleException()
                        {
                            MatchedRule = SwitchRules.PokestopSoftban,
                            ReachedValue = softbanCount
                        };
                    }
                }
            }
            else
            {
                softbanCount = 0; //reset softban count
            }

            if (session.LogicSettings.RandomlyPauseAtStops && !doNotRetry)
            {
                if (++_randomStop >= _randomNumber)
                {
                    _randomNumber = _rc.Next(4, 11);
                    _randomStop = 0;
                    int randomWaitTime = _rc.Next(30, 120);
                    await Task.Delay(randomWaitTime, cancellationToken);
                }
            }

        }

        //Please do not change GetPokeStops() in this file, it's specifically set
        //to only find stops within 40 meters for GPX pathing, as we are not going to the pokestops,
        //so do not make it more than 40 because it will never get close to those stops.
        //For non GPX pathing, it returns all pokestops in range.
        private static async Task<Tuple<List<FortData>, List<FortData>>> GetPokeStops(ISession session)
        {
            List<FortData> mapObjects = await UpdateFortsData(session);
            session.AddForts(mapObjects);

            if (!session.LogicSettings.UseGpxPathing)
            {
                if (mapObjects.Count <= 0)
                {
                    // only send this for non GPX because otherwise we generate false positives
                    session.EventDispatcher.Send(new WarnEvent
                    {
                        Message = session.Translation.GetTranslation(TranslationString.FarmPokestopsNoUsableFound)
                    });
                }

                var pokeStops = mapObjects.Where(p => p.Type == FortType.Checkpoint).ToList();
                session.AddVisibleForts(pokeStops);
                session.EventDispatcher.Send(new PokeStopListEvent { Forts = mapObjects });

                var gyms = mapObjects.Where(p => p.Type == FortType.Gym).ToList();
                //   session.EventDispatcher.Send(new PokeStopListEvent { Forts = mapObjects });
                return Tuple.Create(pokeStops, gyms);
            }

            if (mapObjects.Count > 0)
            {
                // only send when there are stops for GPX because otherwise we send empty arrays often
                session.EventDispatcher.Send(new PokeStopListEvent { Forts = mapObjects });
            }
            // Wasn't sure how to make this pretty. Edit as needed.
            return Tuple.Create(
                mapObjects.Where(
                    i => i.Type == FortType.Checkpoint &&
                        ( // Make sure PokeStop is within 40 meters or else it is pointless to hit it
                            LocationUtils.CalculateDistanceInMeters(
                                session.Client.CurrentLatitude, session.Client.CurrentLongitude,
                                i.Latitude, i.Longitude) < 40) ||
                        session.LogicSettings.MaxTravelDistanceInMeters == 0
                ).ToList(),
                mapObjects.Where(p => p.Type == FortType.Gym && LocationUtils.CalculateDistanceInMeters(
                                session.Client.CurrentLatitude, session.Client.CurrentLongitude,
                                p.Latitude, p.Longitude) < 40).ToList()
                );
        }

        public static async Task<List<FortData>> UpdateFortsData(ISession session)
        {
            var mapObjects = await session.Client.Map.GetMapObjects();
            session.AddForts(mapObjects.MapCells.SelectMany(p => p.Forts).ToList());

            var pokeStops = mapObjects.MapCells.SelectMany(i => i.Forts)
                .Where(
                    i =>
                        (i.Type == FortType.Checkpoint || i.Type == FortType.Gym) &&
                        i.CooldownCompleteTimestampMs < DateTime.UtcNow.ToUnixTime() &&
                        (
                            LocationUtils.CalculateDistanceInMeters(
                                session.Client.CurrentLatitude, session.Client.CurrentLongitude,
                                i.Latitude, i.Longitude) < session.LogicSettings.MaxTravelDistanceInMeters) ||
                        session.LogicSettings.MaxTravelDistanceInMeters == 0
                );

            return pokeStops.ToList();
        }
    }
}