﻿using System;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using Models;
using VirtualFlowersMVC.Data;
using VirtualFlowers;

namespace VirtualFlowersMVC.Controllers
{
    public class RanksController : Controller
    {
        private DatabaseContext _db = new DatabaseContext();
        private dataWorker _dataWorker = new dataWorker();

        // GET: Ranks
        public ActionResult Index()
        {
            return View(_db.RankingList.OrderByDescending(p => p.DateOfRank).ToList());
        }
        
        public ActionResult NewRank()
        {
            return View();
        }

        [HttpPost]
        public ActionResult NewRank(string url)
        {
            // Add to Xml file
            Utility.Utility.AddToRankingListsXml(url);
            
            // And scrape it
            Program.GetRankingList(url);

            return RedirectToAction("Index");
        }

        public ActionResult ScrapeFromXml()
        {
            var RankingList = Utility.Utility.GetRankingListsFromXml();
            foreach (var url in RankingList.Url)
            {
                Program.GetRankingList(url);
            }
            
            return RedirectToAction("Index");
        }

        // GET: Ranks/Details/5
        public ActionResult Details(Guid? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            
            var result = _dataWorker.GetRankingList((Guid)id);
            if (result != null && result.Count > 0)
                ViewBag.Date = _db.RankingList.FirstOrDefault(p => p.RankingListId == id).DateOfRank.ToShortDateString();

            return View(result);
        }

        // GET: Ranks/Create
        public ActionResult Create()
        {
            return View();
        }

        // GET: Ranks/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Rank rank = _db.Rank.Find(id);
            if (rank == null)
            {
                return HttpNotFound();
            }
            return View(rank);
        }

        // POST: Ranks/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Rank rank = _db.Rank.Find(id);
            _db.Rank.Remove(rank);
            _db.SaveChanges();
            return RedirectToAction("Index");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
