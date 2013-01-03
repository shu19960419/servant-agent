﻿using System;
using System.Collections.Generic;
using System.Linq;
using Servant.Business.Helpers;
using Servant.Business.Objects;
using Servant.Business.Objects.Enums;
using Servant.Business.Services;
using Nancy.Responses;
using Nancy.ModelBinding;
using Servant.Manager.Helpers;

namespace Servant.Manager.Modules
{
    public class SitesModule : BaseModule
    {
        public SitesModule(LogEntryService logEntryService, ApplicationErrorService applicationErrorService) : base("/sites/")
        {
            var siteManager = new SiteManager();

            Get["/"] = p  => {
                var sites = siteManager.GetSites();
                Model.Sites = sites;
                return View["Index", Model];
            };

            Get["/create/"] = p => {
                var site = new Site() { RawBindings = new[] {""} };
                Model.Site = site;
                Model.ApplicationPools = siteManager.GetApplicationPools();
                Model.Certificates = siteManager.GetCertificates().Select(x => x.FriendlyName);
                return View["Create", Model];
            };
            
            Post["/create/"] = p => {
                var site = this.Bind<Site>();
                Model.Site = site;
                Model.ApplicationPools = siteManager.GetApplicationPools();
                Model.Certificates = siteManager.GetCertificates().Select(x => x.FriendlyName);
                site.RawBindings = Request.Form.RawBindings.ToString().Split(',');

                if(string.IsNullOrWhiteSpace(site.Name))
                    AddPropertyError("name", "Name is required.");

                if (site.Name != null && siteManager.GetSiteByName(site.Name) != null)
                    AddPropertyError("name", "There's already a site with this name.");

                if(string.IsNullOrWhiteSpace(site.SitePath))
                    AddPropertyError("sitepath", "Site path is required.");

                if(site.SitePath != null && !FileSystemHelper.DirectoryExists(site.SitePath))
                    AddPropertyError("sitepath", "The entered directory doesn't exist.");

                if(site.RawBindings == null)
                    AddPropertyError("bindings", "Minimum 1 binding is required.");
                else
                {
                    for (int i = 0; i < site.RawBindings.Length; i++)
                    {
                        var binding = site.RawBindings[i];
                        var finalizedBinding = BindingHelper.SafeFinializeBinding(binding);

                        if (finalizedBinding == null)
                        {
                            AddPropertyError("rawbindings[" + i + "]", "The binding is invalid.");
                            continue;
                        }

                        if (siteManager.IsBindingInUse(binding))
                            AddPropertyError("rawbindings", string.Format("The binding {0} is already in use.", binding));
                    }
                }

                site.Bindings = BindingHelper.ConvertRawBindings(Request.Form.RawBindings);

                if(!HasErrors)
                {
                    var result = siteManager.CreateSite(site);

                    if(result == CreateSiteResult.NameAlreadyInUse)
                        AddPropertyError("name", "There's already a site with that name.");

                    if(result == CreateSiteResult.BindingAlreadyInUse)
                        AddPropertyError("httpbindings", "The binding is already in use.");

                    if(result == CreateSiteResult.Failed)
                        AddGlobalError("Something went completely wrong :-/");

                    if(result == CreateSiteResult.Success)
                        return new RedirectResponse("/sites/");
                }

                return View["Create", Model];
            };

            Get[@"/(?<Id>[\d]{1,4})/settings/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                site.RawBindings = site.Bindings.Select(x => x.ToString()).ToArray();

                Model.Certificates = siteManager.GetCertificates().Select(x => x.FriendlyName);
                Model.Site = site;
                Model.ApplicationPools = siteManager.GetApplicationPools();
                return View["Settings", Model];
            };

            Post[@"/(?<Id>[\d]{1,4})/settings/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                
                site.Name = Request.Form.SiteName;
                site.SitePath = Request.Form.SitePath;
                site.ApplicationPool = Request.Form.ApplicationPool;
                site.RawBindings = Request.Form.RawBindings.ToString().Split(',');
                if (site.Bindings == null)
                    AddPropertyError("bindings", "Minimum 1 binding is required.");
                else
                {
                    for(var i = 0; i < site.RawBindings.Length; i++)
                    {
                        var binding = site.RawBindings[i];
                        var finializedBinding = BindingHelper.SafeFinializeBinding(binding);
                        
                        if(finializedBinding == null)
                            AddPropertyError("rawbindings[" + i + "]", string.Format("The binding {0} is invalid.", binding));
                        else if (siteManager.IsBindingInUse(binding, site.IisId))
                            AddPropertyError("rawbindings[" + i + "]", string.Format("The binding {0} is already in use.", binding));
                    }
                }

                Model.ApplicationPools = siteManager.GetApplicationPools();
                Model.Site = site;

                if(!HasErrors)
                {
                    site.Bindings = BindingHelper.ConvertRawBindings(site.RawBindings);
                    siteManager.UpdateSite(site);
                }

                return View["Settings", Model];
            };

            Post[@"/(?<Id>[\d]{1,4})/stop/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                siteManager.StopSite(site);
                AddMessage("Site has been stopped.");
                return new RedirectResponse("/sites/" + site.IisId + "/settings/");
            };

            Post[@"/(?<Id>[\d]{1,4})/start/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                siteManager.StartSite(site);
                AddMessage("Site has been started.");
                return new RedirectResponse("/sites/" + site.IisId + "/settings/");
            };

            Post[@"/(?<Id>[\d]{1,4})/restart/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                siteManager.RestartSite(site.IisId);
                AddMessage("Site has been restarted.");
                return new RedirectResponse("/sites/" + site.IisId + "/settings/");
            };

            Post[@"/(?<Id>[\d]{1,4})/recycle/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                siteManager.RecycleApplicationPoolBySite(site.IisId);
                AddMessage("Application pool has been recycled.");
                return new RedirectResponse("/sites/" + site.IisId + "/settings/");
            };

            Post[@"/(?<Id>[\d]{1,4})/delete/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                siteManager.DeleteSite(site.IisId);
                AddMessage("The site {0} was deleted.", site.Name);
                return new RedirectResponse("/sites/");
            };

            Get[@"/(?<Id>[\d]{1,4})/stats/"] = p =>
            {
                StatsRange range;
                StatsRange.TryParse(Request.Query["r"], true, out range); // Defaults "Today" by position
                Model.Range = range;
                Site site = siteManager.GetSiteById(p.Id);
                DateTime oldestDate = DateTime.UtcNow.AddYears(-100);

                switch (range)
                {
                    case StatsRange.Today:
                        oldestDate = DateTime.UtcNow.Date;
                        Model.ActiveSection = "section1";
                        break;
                    case StatsRange.LastWeek:
                        oldestDate = DateTime.UtcNow.AddDays(-7);
                        Model.ActiveSection = "section2";
                        break;
                    case StatsRange.LastMonth:
                        oldestDate = DateTime.UtcNow.AddDays(-30);
                        Model.ActiveSection = "section3";
                        break;
                    case StatsRange.AllTime:
                        Model.ActiveSection = "section4";
                        break;
                }

                var hasAnyStats = logEntryService.GetCountBySite(site.IisId) != 0;
                Model.HasAnyStats = hasAnyStats;

                if(hasAnyStats)
                {
                    var totalRequests = logEntryService.GetCountBySite(site.IisId, oldestDate);
                    Model.LatestEntries = logEntryService.GetLatestBySite(site, 5);
                    Model.TotalRequests = totalRequests;
                    Model.HasEntries = totalRequests != 0;
                    Model.MostActiveClients = logEntryService.GetMostActiveClientsBySite(site.IisId, oldestDate).ToList();
                    Model.MostExpensiveRequests = logEntryService.GetMostExpensiveRequestsBySite(site.IisId, oldestDate).ToList();
                    Model.MostActiveUrls = logEntryService.GetMostActiveUrlsBySite(site.IisId, oldestDate).ToList();
                }


                Model.Site = site;
                return View["Stats", Model];
            };

            Get[@"/(?<Id>[\d]{1,4})/errors/"] = p => {                
                EventLogHelper.SyncServer();

                StatsRange range;
                var rValue = Request.Query["r"];
                if(rValue == null)
                    range = StatsRange.AllTime;
                else
                    StatsRange.TryParse(rValue, true, out range); // Defaults "Today" by position    
                
                Model.Range = range;
                
                Site site = siteManager.GetSiteById(p.Id);
                var hasAnyErrors = applicationErrorService.GetCountBySite(site.IisId) != 0;
                
                IEnumerable<ApplicationError> errors = null;
                switch (range)
                {
                    default:
                        errors = applicationErrorService.GetBySite(site.IisId);
                        Model.ActiveSection = "section1";
                        break;
                    case StatsRange.Today:
                        errors = applicationErrorService.GetBySite(site.IisId, DateTime.UtcNow.Date);
                        Model.ActiveSection = "section2";
                        break;
                    case StatsRange.LastWeek:
                        errors = applicationErrorService.GetBySite(site.IisId, DateTime.UtcNow.AddDays(-7).Date);
                        Model.ActiveSection = "section3";
                        break;
                    case StatsRange.LastMonth:
                        errors = applicationErrorService.GetBySite(site.IisId, DateTime.UtcNow.AddDays(-30).Date);
                        Model.ActiveSection = "section4";
                        break;
                }

                Model.HasAnyErrors = hasAnyErrors;
                Model.Site = site;
                Model.Exceptions = errors.ToList();
                
                return View["Errors", Model];
            };

            Get[@"/(?<Id>[\d]{1,4})/errors/(?<EventLogId>[\d]{1,7})/"] = p =>{
                Site site = siteManager.GetSiteById(p.Id);
                ApplicationError exception = applicationErrorService.GetById(p.EventLogId);
                var relatedRequests = logEntryService.GetAllRelatedToException(site.IisId, exception.DateTime.ToUniversalTime());
                Model.Site = site;
                Model.Exception = exception;
                Model.RelatedRequests = relatedRequests;

                return View["Error", Model];
            };

            Get[@"/(?<Id>[\d]{1,4})/requests/(?<RequestId>[\d]{1,7})/"] = p =>
            {
                Site site = siteManager.GetSiteById(p.Id);
                var request = logEntryService.GetById(p.RequestId);
                Model.Site = site;
                Model.Request = request;

                return View["Request", Model];
            };
        }
    }
}
