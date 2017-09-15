﻿using VirtualFlowersMVC.Models;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using VirtualFlowers;
using System.Threading.Tasks;
using MemoryCache;

namespace VirtualFlowersMVC.Data
{
    public class dataWorker
    {
        private DatabaseContext _db;
        private Program _program = new Program();

        public dataWorker()
        {
            _db = new DatabaseContext();
        }


        #region MATCHES

        public List<MatchesViewModel> GetMatches(int TeamId)
        {
            var query = _db.Match.Where(k => k.Team1Id == TeamId || k.Team2Id == TeamId).ToList();

            var matches = (from p in query
                           select new MatchesViewModel
                           {
                               Id = p.Id,
                               MatchId = p.MatchId,
                               Date = p.Date,
                               Map = p.Map,
                               Event = p.Event,
                               ResultT1 = p.ResultT1,
                               ResultT2 = p.ResultT2,
                               Team1Id = p.Team1Id,
                               Team1Name = _db.Team.FirstOrDefault(k => k.TeamId == p.Team1Id).TeamName,
                               Team2Id = p.Team2Id,
                               Team2Name = _db.Team.FirstOrDefault(k => k.TeamId == p.Team2Id).TeamName
                           }).OrderByDescending(p => p.MatchId).ToList();

            return matches;
        }

        public Match GetMatchDetails(int? id)
        {
            var result = _db.Match.Find(id);

            return result;
        }

        #endregion

        #region TEAMS

        public List<Team> GetTeamsList()
        {
            var result = _db.Team.OrderBy(p => p.TeamName).ToList();

            return result;
        }

        public Team GetTeamDetails(int? id)
        {
            var result = _db.Team.Find(id);

            return result;
        }

        public string GetTeamName(int id)
        {
            var result = "";

            result = _db.Team.SingleOrDefault(p => p.TeamId == id)?.TeamName;

            return result ?? "";
        }

        public int GetSecondaryTeamId(int TeamId)
        {
            int secondaryTeamId = 0;
            var secondaryTeam = _db.TransferHistory.Where(p => p.NewTeamId == TeamId).OrderByDescending(k => k.TransferDate).FirstOrDefault();
            if (secondaryTeam != null)
                secondaryTeamId = secondaryTeam.OldTeamId;
            return secondaryTeamId;
        }

        #endregion

        #region COMPARE

        public async Task<TeamStatisticPeriodModel> GetTeamPeriodStatistics(int TeamId, List<string> PeriodSelection, ExpectedLineUp expectedLinup, int secondaryTeamId, bool NoCache, int MinFullTeamRanking, string teamRank)
        {
            var result = new TeamStatisticPeriodModel();
            result.TeamId = TeamId;
            result.TeamName = GetTeamName(TeamId);
            result.TeamDifficultyRating = _program.GetRankingValueForTeam(TeamId, DateTime.Now);
            result.TeamRank = teamRank;
            foreach (var period in PeriodSelection)
            {
                await Task.Run(() => result.TeamStatistics.Add(GetTeamPeriodStatistics(TeamId, (PeriodEnum)int.Parse(period), expectedLinup, secondaryTeamId, NoCache, MinFullTeamRanking))).ConfigureAwait(false);
            }

            return result;
        }

        public TeamStatisticModel GetTeamPeriodStatistics(int TeamId, PeriodEnum period, ExpectedLineUp expectedLinup, int secondaryTeamId, bool NoCache, int MinFullTeamRanking)
        {
            var result = new TeamStatisticModel();
            var dTo = DateTime.Now;
            var dFrom = new DateTime();
            var mapList = new List<string> { "nuke", "overpass", "cobblestone", "train", "inferno", "mirage", "cache" };

            switch (period)
            {
                case PeriodEnum.ThreeMonths:
                    dFrom = dTo.AddMonths(-3);
                    result.Period = "3 Months";
                    break;
                case PeriodEnum.SixMonths:
                    dFrom = dTo.AddMonths(-6);
                    result.Period = "6 Months";
                    break;
                case PeriodEnum.Year:
                    dFrom = dTo.AddYears(-1);
                    result.Period = "Year";
                    break;
            }

            result.Maps = GetMapStatistics(TeamId, dFrom, dTo, expectedLinup, secondaryTeamId, NoCache, MinFullTeamRanking);

            foreach (var map in mapList)
            {
                if (!result.Maps.Where(p => p.Map.ToLower() == map).Any())
                    result.Maps.Add(new MapStatisticModel
                    {
                        Map = map,
                        FirstRound1HWinPercent = new Tuple<double, string>(0, ""),
                        FirstRound2HWinPercent = new Tuple<double, string>(0, "")
                    });
            }

            return result;
        }

        public List<MapStatisticModel> GetMapStatistics(int TeamId, DateTime dFrom, DateTime dTo, ExpectedLineUp expectedLinup, int secondaryTeamId, bool NoCache, int MinFullTeamRanking)
        {
            var result = new List<MapStatisticModel>();
            List<Match> fixedMatches = null;

            // Create Cachekey from parameters
            var CACHEKEY = "cacheKey:TeamId=" + TeamId + "-DateFrom=" + dFrom.Date.ToString() + "-dTo=" + dTo.Date.ToString();

            // If we have object in cache, return it
            if (NoCache && Cache.Exists(CACHEKEY))
                fixedMatches = (List<Match>)Cache.Get(CACHEKEY);
            else
            {
                var matches = _db.Match
                    .Where(p => (p.Team1Id == TeamId || p.Team2Id == TeamId)
                    && (p.Date >= dFrom.Date && p.Date <= dTo.Date)).ToList();

                // Fix matches so that "our" team is always Team1, to simplify later query
                fixedMatches = FixTeamMatches(matches, TeamId);

                if (secondaryTeamId > 0)
                {
                    // Get matches for secondaryTeamId
                    var secondaryMatches = _db.Match
                    .Where(p => (p.Team1Id == secondaryTeamId || p.Team2Id == secondaryTeamId)
                    && (p.Date >= dFrom.Date && p.Date <= dTo.Date)).ToList();

                    // Fix matches for secondaryMatches
                    var fixedsecondaryMatches = FixTeamMatches(secondaryMatches, secondaryTeamId);

                    // Add matches from secondaryTeamId to fixedMatches
                    fixedMatches.AddRange(fixedsecondaryMatches);
                }
            }
            
            if (!string.IsNullOrEmpty(CACHEKEY))
            {
                if (!Cache.Exists(CACHEKEY))
                {
                    int storeTime = 1000 * 3600 * 24 * 2; // store 2 days
                    Cache.Store(CACHEKEY, fixedMatches, storeTime);
                }
                else
                    Cache.Update(CACHEKEY, fixedMatches);
            }

            result = CalculateResults(TeamId, fixedMatches, expectedLinup, secondaryTeamId, MinFullTeamRanking);
            
            return result;
        }

        public List<MapStatisticModel> CalculateResults(int TeamId, List<Match> fixedMatches, ExpectedLineUp expectedLinup, int secondaryTeamId, int MinFullTeamRanking)
        {
            if (MinFullTeamRanking > 0)
            {
                if (MinFullTeamRanking == 5)
                {
                    fixedMatches = fixedMatches.Where(p => expectedLinup.Players.Where(x => p.T1Player1Id == x.PlayerId).Any()
                    && expectedLinup.Players.Where(x => p.T1Player2Id == x.PlayerId).Any()
                    && expectedLinup.Players.Where(x => p.T1Player3Id == x.PlayerId).Any()
                    && expectedLinup.Players.Where(x => p.T1Player4Id == x.PlayerId).Any()
                    && expectedLinup.Players.Where(x => p.T1Player5Id == x.PlayerId).Any()).ToList();
                }
                else if(MinFullTeamRanking == 4)
                {
                        fixedMatches = fixedMatches.Where(p => (expectedLinup.Players.Where(x => p.T1Player1Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player2Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player3Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player4Id == x.PlayerId).Any())
                        ||
                        (expectedLinup.Players.Where(x => p.T1Player1Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player2Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player3Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player5Id == x.PlayerId).Any())
                        ||
                        (expectedLinup.Players.Where(x => p.T1Player1Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player2Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player4Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player5Id == x.PlayerId).Any())
                        ||
                        (expectedLinup.Players.Where(x => p.T1Player1Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player3Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player4Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player5Id == x.PlayerId).Any())
                        ||
                        (expectedLinup.Players.Where(x => p.T1Player2Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player3Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player4Id == x.PlayerId).Any()
                        && expectedLinup.Players.Where(x => p.T1Player5Id == x.PlayerId).Any())).ToList();
                }
            }

            // Group list by maps.
            var groupedbymaps = fixedMatches.GroupBy(k => k.Map, StringComparer.InvariantCultureIgnoreCase).ToList();

            var result = groupedbymaps.Select(n => new MapStatisticModel
            {
                Map = n.Key,
                TitleMapMatches = GetTitleMapMatches(fixedMatches.Where(p => p.Map == n.Key).OrderByDescending(p => p.Date).ToList()),
                TotalWins = n.Count(p => p.ResultT1 > p.ResultT2),
                TotalLosses = n.Count(p => p.ResultT2 > p.ResultT1),
                WinPercent = Math.Round(n.Count(p => p.ResultT1 > p.ResultT2) / (double)n.Count() * 100, 1),
                // *** Get Average Win rounds ***
                AverageWinRounds = Math.Round(n.Sum(p => p.ResultT1) / (double)n.Count(), 1),
                // *** Get Average Loss rounds ***
                AverageLossRounds = Math.Round(n.Sum(p => p.ResultT2) / (double)n.Count(), 1),
                // *** Get Average Win rounds when team has won ***
                AverageWinRoundsWhenWin = Math.Round(n.Where(p => p.ResultT1 > p.ResultT2) // Where team won
                    .Sum(p => p.ResultT1) / // Sum team rounds
                    (double)n.Count(p => p.ResultT1 > p.ResultT2), 1), // Divided total games we won
                // *** Get Average Loss rounds when team has won ***
                AverageLossRoundsWhenWin = Math.Round(n.Where(p => p.ResultT1 > p.ResultT2) // Where team won
                    .Sum(p => p.ResultT2) / // Sum opponents rounds
                    (double)n.Count(p => p.ResultT1 > p.ResultT2), 1), // Divided total games we won
                // *** Get Average Win rounds when team has lost ***
                AverageWinRoundsWhenLoss = Math.Round(n.Where(p => p.ResultT1 < p.ResultT2) // Where team lost
                    .Sum(p => p.ResultT1) / // Sum team rounds
                    (double)n.Count(p => p.ResultT1 < p.ResultT2), 1), // Divided total games we lost
                // *** Get Average Loss rounds when team has lost ***
                AverageLossRoundsWhenLoss = Math.Round(n.Where(p => p.ResultT1 < p.ResultT2) // Where team lost
                    .Sum(p => p.ResultT2) / // Sum opponents rounds
                    (double)n.Count(p => p.ResultT1 < p.ResultT2), 1), // Divided total games we lost
                DifficultyRating = Math.Round(n.Sum(p => p.Team2RankValue) / (double)n.Count(), 2),
                DiffTitleGroupBy = GetDiffTitleGroupBy(n.ToList()),
                FullTeamRanking = GetFullTeamPercent(TeamId, n.ToList(), expectedLinup, secondaryTeamId),
                FullTeamGroupBy = GetFullTeamTitle(TeamId, n.ToList(), expectedLinup, secondaryTeamId),
                FirstRound1HWinPercent = GetFirstRoundStats(TeamId, n.ToList(), true),
                FirstRound2HWinPercent = GetFirstRoundStats(TeamId, n.ToList(), false)
            }).OrderByDescending(n => n.WinPercent).ToList();

            return result;
        }

        private Tuple<double, string> GetFirstRoundStats(int TeamId, List<Match> Map, bool bFirstHalf)
        {
            Tuple<double, string> result = new Tuple<double, string>(0, "");

            double WinPercent = 0.0;
            int WinCt = 0;
            int LossCt = 0;
            int WinTerr = 0;
            int LossTerr = 0;
            if (bFirstHalf)
            {
                WinPercent = Math.Round(Map.Count(p => p.FirstRound1HWinTeamId == TeamId) / (double)Map.Count() * 100, 0);
                WinCt = Map.Count(p => p.FirstRound1HWinCt == true && p.FirstRound1HWinTeamId == TeamId);
                LossCt = Map.Count(p => p.FirstRound1HWinCt == false && p.FirstRound1HWinTeamId != TeamId);
                WinTerr = Map.Count(p => p.FirstRound1HWinTerr == true && p.FirstRound1HWinTeamId == TeamId);
                LossTerr = Map.Count(p => p.FirstRound1HWinTerr == false && p.FirstRound1HWinTeamId != TeamId);
            }
            else
            {
                WinPercent = Math.Round(Map.Count(p => p.FirstRound2HWinTeamId == TeamId) / (double)Map.Count() * 100, 0);
                WinCt = Map.Count(p => p.FirstRound2HWinCT == true && p.FirstRound2HWinTeamId == TeamId);
                LossCt = Map.Count(p => p.FirstRound2HWinCT == false && p.FirstRound2HWinTeamId != TeamId);
                WinTerr = Map.Count(p => p.FirstRound2HWinTerr == true && p.FirstRound2HWinTeamId == TeamId);
                LossTerr = Map.Count(p => p.FirstRound2HWinTerr == false && p.FirstRound2HWinTeamId != TeamId);
            }
            
            var returnSymbol = "&#010;";
            var Title = $"CT: {WinCt} / {LossCt}{returnSymbol}Terr: {WinTerr} / {LossTerr}";
            
            result = new Tuple<double, string>(WinPercent, Title);
            return result;
        }


        private double GetFullTeamPercent(int TeamId, List<Match> Map, ExpectedLineUp expectedLinup, int secondaryTeamId)
        {
            double Accumulator = 0;
            double AvFTR = 0;

            // Calculate how many of expectedLinup played each match.
            foreach (var match in Map)
            {
                int FTRank = 0;
                if ((match.Team1Id == TeamId || match.Team1Id == secondaryTeamId) && expectedLinup != null)
                {
                    if (secondaryTeamId > 0)
                    {
                        if (expectedLinup.Players.Where(p => (p.TeamID == TeamId || p.TeamID == secondaryTeamId) && p.PlayerId == match.T1Player1Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => (p.TeamID == TeamId || p.TeamID == secondaryTeamId) && p.PlayerId == match.T1Player2Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => (p.TeamID == TeamId || p.TeamID == secondaryTeamId) && p.PlayerId == match.T1Player3Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => (p.TeamID == TeamId || p.TeamID == secondaryTeamId) && p.PlayerId == match.T1Player4Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => (p.TeamID == TeamId || p.TeamID == secondaryTeamId) && p.PlayerId == match.T1Player5Id).Any())
                            FTRank += 1;
                    }
                    else
                    {
                        if (expectedLinup.Players.Where(p => p.TeamID == TeamId && p.PlayerId == match.T1Player1Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => p.TeamID == TeamId && p.PlayerId == match.T1Player2Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => p.TeamID == TeamId && p.PlayerId == match.T1Player3Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => p.TeamID == TeamId && p.PlayerId == match.T1Player4Id).Any())
                            FTRank += 1;
                        if (expectedLinup.Players.Where(p => p.TeamID == TeamId && p.PlayerId == match.T1Player5Id).Any())
                            FTRank += 1;
                    }

                    // How many played this map
                    match.T1FTR = FTRank;

                    // Add to Accumulator for total average.
                    Accumulator += FTRank;
                }
            }

            // Av. played
            if (Accumulator > 0)
                AvFTR = Accumulator / Map.Count;

            return AvFTR;
        }

        private string GetFullTeamTitle(int TeamId, List<Match> Map, ExpectedLineUp expectedLinup, int secondaryTeamId)
        { 
            var rankList = new List<Tuple<double, string>>();
            var FTRankList = Map.GroupBy(p => p.T1FTR).ToList();
            var returnSymbol = "&#010;";
            var Title = "";

            // Group by FTRating.
            foreach (var n in FTRankList)
            {
                var TotalWins = n.Count(p => p.ResultT1 > p.ResultT2);
                var TotalLosses = n.Count(p => p.ResultT2 > p.ResultT1);
                var WinPercent = Math.Round(n.Count(p => p.ResultT1 > p.ResultT2) / (double)n.Count() * 100, 1);
                var AverageWinRounds = Math.Round(n.Sum(p => p.ResultT1) / (double)n.Count(), 1);
                var AverageLossRounds = Math.Round(n.Sum(p => p.ResultT2) / (double)n.Count(), 1);
                rankList.Add(new Tuple<double, string>(n.Key, $"{TotalWins} / {TotalLosses} {WinPercent}% av.r: {AverageWinRounds} / {AverageLossRounds}"));
            }

            // Order the list by FTRating
            var orderedList = rankList.OrderByDescending(p => p.Item1).ToList();

            // Create the title "hover" list
            foreach (Tuple<double, string> tup in orderedList)
            {
                Title += string.IsNullOrEmpty(Title) ? " " : " " + returnSymbol + " ";
                Title += tup.Item1 + " - " + tup.Item2;
            }
            
            return Title;
        }

        private string GetDiffTitleGroupBy(List<Match> Map)
        {
            var result = "";
            var diffList = new List<Tuple<double, string>>();
            var returnSymbol = "&#010;";

            // Group by Difficulty rating.
            var diffRankList = Map.GroupBy(p => p.Team2RankValue).ToList();
            foreach (var n in diffRankList)
            {
                var TotalWins = n.Count(p => p.ResultT1 > p.ResultT2);
                var TotalLosses = n.Count(p => p.ResultT2 > p.ResultT1);
                var WinPercent = Math.Round(n.Count(p => p.ResultT1 > p.ResultT2) / (double)n.Count() * 100, 1);
                var AverageWinRounds = Math.Round(n.Sum(p => p.ResultT1) / (double)n.Count(), 1);
                var AverageLossRounds = Math.Round(n.Sum(p => p.ResultT2) / (double)n.Count(), 1);
                diffList.Add(new Tuple<double, string>(n.Key, $"{TotalWins} / {TotalLosses} {WinPercent}% av.r: {AverageWinRounds} / {AverageLossRounds}"));
            }

            // Order the list by difficulty
            var orderedList = diffList.OrderByDescending(p => p.Item1).ToList();

            // Create the title "hover" list
            foreach (Tuple<double,string> tup in orderedList)
            {
                result += string.IsNullOrEmpty(result) ? " " : " " + returnSymbol + " ";
                result += tup.Item1 == 1? tup.Item1 + ".0" + " - " + tup.Item2 : tup.Item1 + " - " + tup.Item2;
            }

            return result;
        }

        private string GetTitleMapMatches(List<Match> MapMatches)
        {
            var result = "";
            var returnSymbol = "&#010;";
            
            foreach (var match in MapMatches)
            {
                var opponentName = "";
                if(_db.Team.Any(p => p.TeamId == match.Team2Id))
                    opponentName = _db.Team.SingleOrDefault(p => p.TeamId == match.Team2Id).TeamName;
                result += string.IsNullOrEmpty(result) ? " " : " " + returnSymbol + " ";
                result += match.Date.ToShortDateString() + " - " + match.ResultT1 + " - " + match.ResultT2 + " " + opponentName + " (" + match.Team2RankValue + ")";
            }
            
            return result;
        }

        /// <summary>
        /// Take matches query and set "our" (TeamId) team always as Team1
        /// </summary>
        /// <param name="matches"></param>
        /// <param name="TeamId"></param>
        /// <returns></returns>
        public List<Match> FixTeamMatches(List<Match> matches, int TeamId)
        {
            var result = matches.Select(n => new Match
            {
                Id = n.Id,
                MatchId = n.MatchId,
                Date = n.Date,
                Map = n.Map,
                Event = n.Event,
                ResultT1 = n.Team1Id == TeamId ? n.ResultT1 : n.ResultT2, // 
                ResultT2 = n.Team1Id == TeamId ? n.ResultT2 : n.ResultT1,
                FirstRound1HWinTeamId = n.FirstRound1HWinTeamId,
                FirstRound1HWinCt = n.FirstRound1HWinCt,
                FirstRound1HWinTerr =  n.FirstRound1HWinTerr,
                FirstRound2HWinTeamId = n.FirstRound2HWinTeamId,
                FirstRound2HWinTerr = n.FirstRound2HWinTerr,
                FirstRound2HWinCT = n.FirstRound2HWinCT,
                Team1Id = n.Team1Id == TeamId ? n.Team1Id : n.Team2Id,
                Team1RankValue = n.Team1Id == TeamId ? n.Team1RankValue : n.Team2RankValue,
                T1Player1Id = n.Team1Id == TeamId ? n.T1Player1Id : n.T2Player1Id,
                T1Player2Id = n.Team1Id == TeamId ? n.T1Player2Id : n.T2Player2Id,
                T1Player3Id = n.Team1Id == TeamId ? n.T1Player3Id : n.T2Player3Id,
                T1Player4Id = n.Team1Id == TeamId ? n.T1Player4Id : n.T2Player4Id,
                T1Player5Id = n.Team1Id == TeamId ? n.T1Player5Id : n.T2Player5Id,
                Team2Id = n.Team1Id == TeamId ? n.Team2Id : n.Team1Id,
                Team2RankValue = n.Team1Id == TeamId ? n.Team2RankValue : n.Team1RankValue,
                T2Player1Id = n.Team1Id == TeamId ? n.T2Player1Id : n.T1Player1Id,
                T2Player2Id = n.Team1Id == TeamId ? n.T2Player2Id : n.T1Player2Id,
                T2Player3Id = n.Team1Id == TeamId ? n.T2Player3Id : n.T1Player3Id,
                T2Player4Id = n.Team1Id == TeamId ? n.T2Player4Id : n.T1Player4Id,
                T2Player5Id = n.Team1Id == TeamId ? n.T2Player5Id : n.T1Player5Id,
            }).ToList();

            return result;
        }

        public void GenerateSuggestedMaps(ref CompareStatisticModel model)
        {
            if (model.Teams.Count == 2)
            {
                foreach (var period in model.Teams[0].TeamStatistics)
                {
                    var PeriodName = period.Period;
                    foreach (var map in period.Maps)
                    {
                        var resultMap = FindMatchingMaps(map, PeriodName, model.Teams[1]);
                        if (resultMap != null)
                            period.SuggestedMaps.Add(resultMap);
                    }

                    // Order by highest rank
                    if (period.SuggestedMaps != null && period.SuggestedMaps.Count > 1)
                        period.SuggestedMaps = period.SuggestedMaps.OrderByDescending(p => p.SuggestedRank).ToList();
                }


                foreach (var period in model.Teams[1].TeamStatistics)
                {
                    var PeriodName = period.Period;
                    foreach (var map in period.Maps)
                    {
                        var resultMap = FindMatchingMaps(map, PeriodName, model.Teams[0]);
                        if (resultMap != null)
                            period.SuggestedMaps.Add(resultMap);
                    }

                    // Order by highest rank
                    if (period.SuggestedMaps != null && period.SuggestedMaps.Count > 1)
                        period.SuggestedMaps = period.SuggestedMaps.OrderByDescending(p => p.SuggestedRank).ToList();
                }
            }
        }

        public SuggestedMapModel FindMatchingMaps(MapStatisticModel MapA, string PeriodName, TeamStatisticPeriodModel TeamB)
        {
            bool MapFound = false;

            foreach (var period in TeamB.TeamStatistics)
            {
                // Find matching period
                if (PeriodName == period.Period)
                {
                    // Try to find matching map
                    foreach (var MapB in period.Maps)
                    {
                        if(MapA.Map.ToLower() == MapB.Map.ToLower())
                        {
                            MapFound = true;
                            // If found compare maps
                            return CompareStats(MapA, MapB);
                        }
                    }
                    if (MapFound == false)
                    {
                        // Compare with empty map
                        return CompareStats(MapA, new MapStatisticModel());
                    }
                }
            }

            return null;
        }

        private SuggestedMapModel CompareStats(MapStatisticModel MapA, MapStatisticModel MapB)
        {
            SuggestedMapModel result = null;
            double TFRPoints = 0.0;

            #region SPECIAL CASES
            // Empty record, default 0.5 
            if (MapB.DifficultyRating == 0)
                MapB.DifficultyRating = 0.5;
            var WinPercentA = SetWinPercentSpecialCases(MapA);
            var WinPercentB = SetWinPercentSpecialCases(MapB);
            #endregion

            int WinLossRecord = (MapA.TotalWins - MapA.TotalLosses) - (MapB.TotalWins - MapB.TotalLosses);
            var valuePoint = Math.Round((WinPercentA - WinPercentB) / 10.0);
            
            var diffPoint = (int)Math.Floor((MapA.DifficultyRating - MapB.DifficultyRating) * 10);
            TFRPoints = MapA.FullTeamRanking -  MapB.FullTeamRanking;
            
            if (WinLossRecord > 0 && valuePoint > 0 && MapA.WinPercent >= 50)
            {
                result = new SuggestedMapModel();

                result.Map = MapA.Map;
                result.SuggestedRank = Math.Ceiling((((WinLossRecord + valuePoint) / 2.0) + diffPoint ) * (MapA.FullTeamRanking * 0.2));
                result.WinLossRecord = WinLossRecord;
                result.WinPercent = Math.Round(WinPercentA - WinPercentB, 1);
                result.DifficultyRating = Math.Round((MapA.DifficultyRating - MapB.DifficultyRating) * 10, 1);
                result.TFRating = Math.Round(TFRPoints, 1);
            }

            return result;
        }

        private double SetWinPercentSpecialCases(MapStatisticModel Map)
        {
            // If extra few matches, set winpercent manually
            if (Map.TotalWins + Map.TotalLosses < 2)
            {
                if (Map.TotalWins + Map.TotalLosses == 0) // 50% if no games
                    return 50;
                else if (Map.TotalWins == 1) // 1-0 record 67%
                    return 67;
                else // 0-1 record 33%
                    return 33;
            }
            else
                return Map.WinPercent;
        }

        public int AddScrapedMatch(ScrapedMatches scrapedMatch, int MinFTR)
        {
            var model = new ScrapedMatches();
            if (_db.ScrapedMatches.Any(p => p.MatchId == scrapedMatch.MatchId))
            {
                model = _db.ScrapedMatches.Single(p => p.MatchId == scrapedMatch.MatchId);
                scrapedMatch.Id = model.Id;
                model.MatchId = scrapedMatch.MatchId;
                model.MatchUrl = scrapedMatch.MatchUrl;
                model.Name = scrapedMatch.Name;
                model.SportName = scrapedMatch.SportName;
                model.Start = scrapedMatch.Start;
                model.Json = MinFTR==0 || MinFTR ==-1? scrapedMatch.Json : model.Json;
                model.Json4MinFTR = MinFTR == 4 || MinFTR == -1 ? scrapedMatch.Json4MinFTR : model.Json4MinFTR;
                model.Json5MinFTR = MinFTR == 5 || MinFTR == -1 ? scrapedMatch.Json5MinFTR : model.Json5MinFTR;
            }
            else
                _db.ScrapedMatches.Add(scrapedMatch);
            _db.SaveChanges();

            return scrapedMatch.Id;
        }

        #endregion

        #region RANKING

        public List<RankViewModel> GetRankingList(Guid listID)
        {
            var result = (from rank in _db.Rank
                where rank.RankingListId == listID
                select new RankViewModel
                {
                    RankPosition = rank.RankPosition,
                    TeamId = rank.TeamId,
                    TeamName = _db.Team.FirstOrDefault(k => k.TeamId == rank.TeamId).TeamName,
                    Points = rank.Points
                }).ToList();

            return result;
        }

        #endregion

        #region SCRAPED MATCHES

        public List<ScrapedMatches> GetScrapedMatches(int days)
        {
            List<ScrapedMatches> result = new List<ScrapedMatches>();
            DateTime dDate = DateTime.Now.AddDays(days * -1);
            result = _db.ScrapedMatches.Where(p => p.Start > dDate).ToList();

            return result;
        }

        public ScrapedMatches GetScrapedMatch(int id)
        {
            ScrapedMatches result = new ScrapedMatches();
            result = _db.ScrapedMatches.Single(p => p.Id == id);

            return result;
        }

        #endregion
    }

    public enum PeriodEnum
    {
        ThreeMonths = 3,
        SixMonths = 6,
        Year = 12
    }
}