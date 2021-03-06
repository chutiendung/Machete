﻿#region COPYRIGHT
// File:     HirerWorkOrderController.cs
// Author:   Savage Learning, LLC.
// Created:  2012/06/17 
// License:  GPL v3
// Project:  Machete.Web
// Contact:  savagelearning
// 
// Copyright 2011 Savage Learning, LLC., all rights reserved.
// 
// This source file is free software, under either the GPL v3 license or a
// BSD style license, as supplied with this software.
// 
// This source file is distributed in the hope that it will be useful, but 
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY 
// or FITNESS FOR A PARTICULAR PURPOSE. See the license files for details.
//  
// For details please refer to: 
// http://www.savagelearning.com/ 
//    or
// http://www.github.com/jcii/machete/
// 
#endregion

using AutoMapper;
using Machete.Domain;
using Machete.Service;
using Machete.Web.Helpers;
using Machete.Web.Helpers.PayPal;
using Machete.Web.Resources;
using Microsoft.AspNet.Identity;
using Newtonsoft.Json.Linq;
using NLog;
using PayPal.Exception;
using PayPal.PayPalAPIInterfaceService.Model;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Web.Mvc;
using System.Web.Routing;

namespace Machete.Web.Controllers
{
    [ElmahHandleError]
    public class HirerWorkOrderController : MacheteController
    {
        private readonly IWorkOrderService woServ;
        private readonly IEmployerService eServ;
        private readonly IWorkerService wServ;
        private readonly IWorkerRequestService wrServ;
        private readonly IWorkAssignmentService waServ;
        private readonly ILookupCache lcache;
        CultureInfo CI;
        Logger log = LogManager.GetCurrentClassLogger();
        LogEventInfo levent = new LogEventInfo(LogLevel.Debug, "HirerWorkOrderController", "");

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="woServ">Work Order service</param>
        /// <param name="waServ">Work Assignment service</param>
        /// <param name="eServ">Employer service</param>
        /// <param name="wServ">Worker service</param>
        /// <param name="wrServ">Worker request service</param>
        /// <param name="lcache">Lookup cache</param>
        public HirerWorkOrderController(IWorkOrderService woServ,
                                   IWorkAssignmentService waServ,
                                   IEmployerService eServ,
                                   IWorkerService wServ,
                                   IWorkerRequestService wrServ,
                                   ILookupCache lcache)
        {
            this.woServ = woServ;
            this.eServ = eServ;
            this.wServ = wServ;
            this.waServ = waServ;
            this.wrServ = wrServ;
            this.lcache = lcache;
        }

        /// <summary>
        /// Initialize controller
        /// </summary>
        /// <param name="requestContext">Request Context</param>
        protected override void Initialize(RequestContext requestContext)
        {
            base.Initialize(requestContext);
            this.CI = (CultureInfo)Session["Culture"];
        }

        #region Index

        /// <summary>
        /// HTTP GET /HirerWorkOrder/Index
        /// </summary>
        /// <returns>MVC Action Result</returns>
        [Authorize(Roles = "Hirer")]
        public ActionResult Index()
        {
            ViewBag.employerId = null;

            // Retrieve employer ID of signed in Employer
            string employerID = HttpContext.User.Identity.GetUserId();

            Employer employer = eServ.GetRepo().GetAllQ().Where(e => e.onlineSigninID == employerID).FirstOrDefault();
            if (employer != null)
            {
                ViewBag.employerId = employer.ID;
            }
             
            return View("Index");
        }

        #endregion

        #region List

        /// <summary>
        /// HTTP GET /HirerWorkOrder/List
        /// </summary>
        /// <returns>MVC Action Result</returns>
        [Authorize(Roles = "Hirer")]
        public ActionResult List()
        {
            return View();
        }

        #endregion

        #region Ajaxhandler

        /// <summary>
        /// Provides json grid of work orders -- used with the hirerworkorders/index view
        /// </summary>
        /// <param name="param">contains parameters for filtering</param>
        /// <returns>JsonResult for DataTables consumption</returns>
        [Authorize(Roles = "Hirer")]
        public ActionResult AjaxHandler(jQueryDataTableParam param)
        {
            var vo = Mapper.Map<jQueryDataTableParam, viewOptions>(param);
            vo.CI = this.CI;

            // Retrieve employer ID of signed in Employer
            string employerID = HttpContext.User.Identity.GetUserId();

            Employer employer = eServ.GetRepo().GetAllQ().Where(e => e.onlineSigninID == employerID).FirstOrDefault();
            if (employer != null)
            {
                vo.EmployerID = employer.ID;
            }
            else
            {
                // TODO: add error processing.
            }

            //Get all the records
            dataTableResult<WorkOrder> dtr = woServ.GetIndexView(vo);

            // TODO: investigate this
            param.showOrdersWorkers = true;

            return Json(new
            {
                sEcho = param.sEcho,
                iTotalRecords = dtr.totalCount,
                iTotalDisplayRecords = dtr.filteredCount,
                aaData = from p in dtr.query
                         select dtResponse (p, param.showOrdersWorkers)
            },
            JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Returns Work Order object to AjaxHandler - presented on the WorkOrder Details tab
        /// </summary>
        /// <param name="wo">WorkOrder</param>
        /// <param name="showWorkers">bool flag determining whether the workers associated with the WorkOrder should be retrieved</param>
        /// <returns>Work Order </returns>
        public object dtResponse( WorkOrder wo, bool showWorkers)
        {
            // tabref = "/HirerWorkOrder/Edit" + Convert.ToString(wo.ID),
            int ID = wo.ID;
            return new
            {
                tabref = "/HirerWorkOrder/View/" + Convert.ToString(wo.ID),
                tablabel = Machete.Web.Resources.WorkOrders.tabprefix + wo.getTabLabel(),
                EID = Convert.ToString(wo.EmployerID),
                WOID = System.String.Format("{0,5:D5}", wo.paperOrderNum), // Note: paperOrderNum defaults to the value of the WO when a paperOrderNum is not provided
                dateTimeofWork = wo.dateTimeofWork.ToString(),
                status = lcache.textByID(wo.status, CI.TwoLetterISOLanguageName),
                WAcount = wo.workAssignments.Count(a => a.workOrderID == ID).ToString(),
                contactName = wo.contactName,
                workSiteAddress1 = wo.workSiteAddress1,
                zipcode = wo.zipcode,
                transportMethod = lcache.textByID(wo.transportMethodID, CI.TwoLetterISOLanguageName),
                displayState = _getDisplayState(wo), // State is used to provide color highlighting to records based on state
                onlineSource = wo.onlineSource ? Shared.True : Shared.False,
                workers = showWorkers ? // Workers is only loaded when showWorkers parameter set to TRUE
                        from w in wo.workAssignments
                        select new
                        {
                            WID = w.workerAssigned != null ? (int?)w.workerAssigned.dwccardnum : null,
                            name = w.workerAssigned != null ? w.workerAssigned.Person.firstname1 : null, // Note: hirers should only have access to the workers first name
                            skill = lcache.textByID(w.skillID, CI.TwoLetterISOLanguageName),
                            hours = w.hours,
                            wage = w.hourlyWage
                        } : null

            };
        }

        /// <summary>
        /// Determines displayState value in WorkOrder/AjaxHandler. Display state is used to provide color highlighting to records based on state.
        /// The displayState is not presented to the user, so don't have to provide internationalization text.
        /// </summary>
        /// <param name="wo">WorkOrder</param>
        /// <returns>status string</returns>
        private string _getDisplayState(WorkOrder wo)
        {
            string status = lcache.textByID(wo.status, "en");

            if (wo.status == WorkOrder.iCompleted)
            {
                // If WO is completed, but 1 (or more) WA aren't assigned - the WO is still Unassigned
                if (wo.workAssignments.Count(wa => wa.workerAssignedID == null) > 0)
                {
                    return "Unassigned";
                }
                // If WO is completed, but 1 (or more) Assigned Worker(s) never signed in, then the WO has been Orphaned
                if (wo.workAssignments.Count(wa => wa.workerAssignedID != null && wa.workerSigninID == null) > 0)
                {
                    return "Orphaned";
                }
            }
            return status;
        }

        #endregion

        #region Create

        /// <summary>
        /// HTTP GET /HirerWorkOrder/Create
        /// </summary>
        /// <returns>MVC Action Result</returns>
        [Authorize(Roles = "Hirer")]
        public ActionResult Create()
        {
            WorkOrder wo = new WorkOrder();
            
            // Retrieve user ID of signed in Employer
            string userID = HttpContext.User.Identity.GetUserId();

            // Retrieve Employer record
            Employer employer = eServ.GetRepo().GetAllQ().Where(e => e.onlineSigninID == userID).FirstOrDefault();
            if (employer != null)
            {
                // Employer has created profile or work order already

                // Associate WO with Employer
                wo.EmployerID = employer.ID;

                // Set up default values from Employer record
                wo.contactName = employer.name;
                wo.phone = employer.phone;
                wo.workSiteAddress1 = employer.address1;
                wo.workSiteAddress2 = employer.address2;
                wo.city = employer.city;
                wo.state = employer.state;
                wo.zipcode = employer.zipcode;
            }

            // Set default values
            wo.dateTimeofWork = DateTime.Today.AddHours(9).AddDays(3); // Set default work time to 9am three days from now
            wo.transportMethodID = Lookups.getDefaultID(LCategory.transportmethod);
            wo.typeOfWorkID = Lookups.getDefaultID(LCategory.worktype);
            wo.status = Lookups.getDefaultID(LCategory.orderstatus);
            wo.timeFlexible = true;
            wo.onlineSource = true;
            wo.disclosureAgreement = false;
            ViewBag.workerRequests = new List<SelectListItem> { };

            // Build Skill lookups
            ViewBag.ID = new int[22];
            ViewBag.text_EN = new string[22];
            ViewBag.text_ES = new string[22];
            ViewBag.wage = new double[22];
            ViewBag.minHour = new int[22];
            ViewBag.workType = new int[22];
            ViewBag.desc_ES = new string[22];
            ViewBag.desc_EN = new string[22];


            //IEnumerable<Lookup> lookup = lcache.getCache();
            List<SelectListEmployerSkills> lookup = Lookups.getOnlineEmployerSkill();

            int counter = 0;

            for (int i = 0; i < lookup.Count(); i++)
            {
                SelectListEmployerSkills lup = lookup.ElementAt(i);
                //Lookup lup = lookup.ElementAt(i);
                //if (lup.ID == 60 || lup.ID == 61 || lup.ID == 62 || lup.ID == 63 || lup.ID == 64 || lup.ID == 65 || lup.ID == 66 || lup.ID == 67 || lup.ID == 68 || lup.ID == 69 || lup.ID == 77 || lup.ID == 83 || lup.ID == 88 || lup.ID == 89 || lup.ID == 118 || lup.ID == 120 || lup.ID == 122 || lup.ID == 128 || lup.ID == 131 || lup.ID == 132 || lup.ID == 133 || lup.ID == 183)
                if (lup.ID == 60 || lup.ID == 61 || lup.ID == 62 || lup.ID == 63 || lup.ID == 64 || lup.ID == 65 || lup.ID == 66 || lup.ID == 67 || lup.ID == 68 || lup.ID == 69 || lup.ID == 77 || lup.ID == 83 || lup.ID == 88 || lup.ID == 89 || lup.ID == 118 || lup.ID == 120 || lup.ID == 122 || lup.ID == 128 || lup.ID == 131 || lup.ID == 132 || lup.ID == 133 || lup.ID == 183)
                {
                    ViewBag.ID[counter] = lup.ID;
                    ViewBag.wage[counter] = lup.wage;
                    ViewBag.minHour[counter] = lup.minHour;
                    ViewBag.workType[counter] = lup.typeOfWorkID;
                    ViewBag.text_EN[counter] = lup.skillDescriptionEn;
                    ViewBag.text_ES[counter] = lup.skillDescriptionEs;
                    counter++;
                }
            }

            return PartialView("Create", wo);
        }

        /// <summary>
        /// POST: /HirerWorkOrder/Create
        /// </summary>
        /// <param name="wo">WorkOrder to create</param>
        /// <param name="userName">User performing action</param>
        /// <param name="workerAssignments">List of worker assignments & requests (stringified JSON object with array of Assignments objects & array of Requests objects</param>
        /// <returns>JSON Object representing new Work Order</returns>
        [HttpPost, UserNameFilter]
        [Authorize(Roles = "Hirer")]
        public ActionResult Create(WorkOrder wo, string userName, string workerAssignments)
        {
            UpdateModel(wo);

            // Retrieve user ID of signed in Employer
            string userID = HttpContext.User.Identity.GetUserId();

            // Retrieve Employer record
            Employer onlineEmployer = eServ.GetRepo().GetAllQ().Where(e => e.onlineSigninID == userID).FirstOrDefault();
            if (onlineEmployer != null)
            {
                // Employer has created profile or work order already

                // Associate WO with Employer
                wo.EmployerID = onlineEmployer.ID;
            }
            else
            {
                // Employer has NOT created profile or work order yet
                Employer employer = new Employer();

                // Set up default values from WO
                employer.name = wo.contactName;
                employer.phone = wo.phone;
                employer.address1 = wo.workSiteAddress1;
                employer.address2 = wo.workSiteAddress2;
                employer.city = wo.city;
                employer.state = wo.state;
                employer.zipcode = wo.zipcode;

                // Set up default online Employer profile
                employer.isOnlineProfileComplete = true;
                employer.onlineSigninID = userID;
                employer.email = HttpContext.User.Identity.GetUserName(); // The Employer's username is their email address
                employer.active = true;
                employer.business = false;
                employer.blogparticipate = false;
                employer.onlineSource = true;
                employer.returnCustomer = false;
                employer.receiveUpdates = true;
                employer.business = false;

                Employer newEmployer = eServ.Create(employer, userName);
                if (newEmployer != null)
                {
                    wo.EmployerID = newEmployer.ID;
                }
            }

            // Set disclosure agreement - to get here, the user had to accept
            wo.disclosureAgreement = true;

            if (workerAssignments == "")
            {
                // Set WA counter 
                wo.waPseudoIDCounter = 0;
            }

            // Create Work Order
            WorkOrder neworder = woServ.Create(wo, userName);

            if (workerAssignments != "")
            {
                // Create Work Assignments
                dynamic parsedWorkerRequests = JObject.Parse(workerAssignments);

                // Set WA counter 
                wo.waPseudoIDCounter = parsedWorkerRequests["assignments"].Count;
                woServ.Save(neworder, userName);

                for (int i = 0; i < parsedWorkerRequests["assignments"].Count; i++)
                {
                    WorkAssignment wa = new WorkAssignment();


                    // Create WA from Employer data
                    wa.workOrderID = neworder.ID;
                    wa.skillID = parsedWorkerRequests["assignments"][i].skillId;
                    wa.hours = parsedWorkerRequests["assignments"][i].hours;
                    wa.weightLifted = parsedWorkerRequests["assignments"][i].weight;
                    wa.hourlyWage = parsedWorkerRequests["assignments"][i].hourlyWage; // TODO: consider looking this up instead - however, this is the value quoted to the customer online
                    wa.pseudoID = i + 1;
                    wa.description = parsedWorkerRequests["assignments"][i].desc;

                    // Set up defaults
                    wa.active = true;
                    wa.englishLevelID = 0; // TODO: note- all incoming online work assignments won't have the proper English level set (this needs to be set by the worker center)
                    wa.surcharge = 0.0;
                    wa.days = 1;
                    wa.qualityOfWork = 0;
                    wa.followDirections = 0;
                    wa.attitude = 0;
                    wa.reliability = 0;
                    wa.transportProgram = 0;

                    WorkAssignment newWa = waServ.Create(wa, userName);
                }

                // TODO: test
                // New Worker Requests to add
                for (int i = 0; i < parsedWorkerRequests["requests"].Count; i++)
                {
                    WorkerRequest wr = new WorkerRequest();

                    // Create Worker Request from Employer data
                    wr.WorkOrderID = neworder.ID;
                    wr.WorkerID = parsedWorkerRequests["requests"][i].workerId;

                    WorkerRequest newWr = wrServ.Create(wr, userName);
                }
            }

            if (neworder.transportFee > 0)
            {
                return View("IndexPrePaypal", neworder);
            }
            else
            {
                return View("IndexComplete", neworder);
            }
        }

        #endregion

        #region Profile

        /// <summary>
        /// HTTP GET /HirerWorkOrder/Profile
        /// </summary>
        /// <returns>MVC Action Result</returns>
        [Authorize(Roles = "Hirer")]
        public ActionResult HirerProfile()
        {
            // Retrieve user ID of signed in Employer
            string userID = HttpContext.User.Identity.GetUserId();

            // Retrieve Employer record
            Employer employer = eServ.GetRepo().GetAllQ().Where(e => e.onlineSigninID == userID).FirstOrDefault();
            if (employer != null)
            {
                // Employer has created profile or work order already
            }
            else
            {
                employer = new Employer();
            }

            // Set default values
            employer.active = true;
            employer.onlineSource = true;
            employer.returnCustomer = false;
            employer.receiveUpdates = true;
            employer.business = false;
            employer.email = HttpContext.User.Identity.Name;
            employer.onlineSigninID = userID;
            employer.isOnlineProfileComplete = true;

            return PartialView("Profile", employer);
        }

        /// <summary>
        /// POST: /HirerWorkOrder/Profile
        /// </summary>
        /// <param name="e">Employer record to update/create</param>
        /// <param name="wo">Profile to create</param>
        /// <param name="userName">User performing action</param>
        [HttpPost, UserNameFilter]
        [Authorize(Roles = "Hirer")]
        public ActionResult HirerProfile(Employer e, string userName)
        {
            UpdateModel(e);

            // Retrieve user ID of signed in Employer
            string userID = HttpContext.User.Identity.GetUserId();

            // Retrieve Employer record
            Employer onlineEmp = eServ.GetRepo().GetAllQ().Where(emp => emp.onlineSigninID == userID).FirstOrDefault();

            if (onlineEmp != null)
            {
                Employer onlineEmployer = eServ.Get(onlineEmp.ID);
                //e.ID = onlineEmployer.ID;
                onlineEmployer.active = true;
                onlineEmployer.address1 = e.address1;
                onlineEmployer.address2 = e.address2;
                onlineEmployer.business = e.business;
                onlineEmployer.businessname = e.businessname;
                onlineEmployer.cellphone = e.cellphone;
                onlineEmployer.city = e.city;
                onlineEmployer.email = e.email;
                onlineEmployer.isOnlineProfileComplete = true;
                onlineEmployer.name = e.name;
                onlineEmployer.onlineSigninID = HttpContext.User.Identity.GetUserId();
                onlineEmployer.onlineSource = true;
                onlineEmployer.phone = e.phone;
                onlineEmployer.receiveUpdates = e.receiveUpdates;
                onlineEmployer.referredby = e.referredby;
                onlineEmployer.referredbyOther = e.referredbyOther;
                onlineEmployer.returnCustomer = e.returnCustomer;
                onlineEmployer.state = e.state;
                onlineEmployer.zipcode = e.zipcode;
                // Employer has created profile already - just need to update profile
                eServ.Save(onlineEmployer, userName);
                return View("Index");
            }
            else
            {
                // Create Employer record
                Employer newEmployer = eServ.Create(e, userName);
                return View("Index");
            }
        }

        #endregion

        #region Edit

        /// <summary>
        /// GET: /HirerWorkOrder/Edit/ID
        /// </summary>
        /// <param name="id">WorkOrder ID</param>
        /// <returns>MVC Action Result</returns>
        [Authorize(Roles = "Hirer")]
        public ActionResult Edit(int id)
        {
            // Retrieve Work Order
            WorkOrder workOrder = woServ.Get(id);

            return PartialView("Edit", workOrder);
        }

        #endregion

        #region View

        /// <summary>
        /// GET: /HirerWorkOrder/PaymentPre
        /// </summary>
        /// <param name="id">WorkOrder ID</param>
        /// <returns>MVC Action Result</returns>
        [UserNameFilter]
        [Authorize(Roles = "Hirer")]
        public ActionResult PaymentPre(int id)
        {
            WorkOrder workOrder = woServ.Get(id);

            return View("IndexPrePaypal", workOrder);
        }

        /// <summary>
        /// POST: /HirerWorkOrder/PaymentPre
        /// </summary>
        /// <param name="id">WorkOrder ID</param>
        /// <param name="userName">User performing action</param>
        /// <returns>MVC Action Result</returns>
        [HttpPost, UserNameFilter]
        [Authorize(Roles = "Hirer")]
        public ActionResult PaymentPre(string orderId, string userName)
        {
            // Retrieve Work Order
            WorkOrder workOrder = woServ.Get(Convert.ToInt32(orderId));

            if (workOrder == null)
            {
                levent.Level = LogLevel.Error;
                levent.Message = "WorkOrder ID not valid Work Order. WO#:" + orderId;
                log.Log(levent);
                return View("IndexError", workOrder);
            }

            double payment = workOrder.transportFee;
            if (payment <= 0.0)
            {
                levent.Level = LogLevel.Error;
                levent.Message = "There is no transportation fee associated with this work order - there is no PayPal transaction required. WO#:" + orderId;
                log.Log(levent);
                return View("IndexError", workOrder);
            }

            PaypalExpressCheckout paypal = new PaypalExpressCheckout();

            SetExpressCheckoutResponseType response = paypal.SetExpressCheckout(payment.ToString());
            if (response != null)
            {
                // # Success values
                if (response.Ack.ToString().Trim().ToUpper().Equals("SUCCESS"))
                {
                    // # Redirecting to PayPal for authorization
                    // Once you get the "Success" response, needs to authorise the
                    // transaction by making buyer to login into PayPal. For that,
                    // need to construct redirect url using EC token from response.
                    // For example,
                    // `redirectURL="https://www.sandbox.paypal.com/cgi-bin/webscr?cmd=_express-checkout&token=" + setExpressCheckoutResponse.Token;`

                    // Save PayPal token
                    workOrder.paypalToken = response.Token;

                    // Save work order updates
                    woServ.Save(workOrder, userName);
                    
                    object paypalConfigSection = null;
                    try
                    {
                        paypalConfigSection = System.Web.Configuration.WebConfigurationManager.GetSection("paypal");
                    }
                    catch (System.Exception ex)
                    {
                        throw new ConfigException("Unable to load 'paypal' section from *.config: " + ex.Message);
                    }

                    if (paypalConfigSection == null)
                    {
                        throw new ConfigException(
                            "Cannot parse *.Config file. Ensure you have configured the 'paypal' section correctly.");
                    }

                    NameValueConfigurationCollection paypalSettings = (NameValueConfigurationCollection)paypalConfigSection.GetType().GetProperty("Settings").GetValue(paypalConfigSection, null);
                    
                    var paypalUrl = paypalSettings["paypalUrl"].Value;
                    var redirectUrl = paypalUrl + "_express-checkout&token=" + response.Token;

                    return new RedirectResult(redirectUrl, false);

                }
                // # Error Values
                else
                {
                    List<ErrorType> errorMessages = response.Errors;
                    foreach (ErrorType error in errorMessages)
                    {
                        levent.Level = LogLevel.Error;
                        levent.Message += error.LongMessage;
                        log.Log(levent);

                        workOrder.paypalErrors += error.ShortMessage;
                    }

                    // Save work order updates
                    woServ.Save(workOrder, userName);

                    return View("IndexError", workOrder);
                }
            }
            else
            {
                levent.Level = LogLevel.Error;
                levent.Message = "The response from PayPal SetExpressCheckoutResponseType API was null. WO#:" + orderId;
                log.Log(levent);
                return View("IndexError", workOrder);
            }

        }

        /// <summary>
        /// GET: /HirerWorkOrder/PaymentPost
        /// </summary>
        /// <param name="token">PayPal token</param>
        /// <param name="payerId">PayPal Payer ID</param>
        /// <param name="userName">User performing action</param>
        /// <returns>MVC Action Result</returns>
        [UserNameFilter]
        [Authorize(Roles = "Hirer")]
        public ActionResult PaymentPost(string token, string payerId, string userName)
        {
            double payment = 0.0;

            // TODO: There was an issue with the WO returned by the first query below - the work order
            // can't be saved unless the work order is retrieved with the woServ.Get() call
            WorkOrder woAll = woServ.GetRepo().GetAllQ().Where(wo => wo.paypalToken == token).FirstOrDefault();
            WorkOrder workOrder = woServ.Get(woAll.ID);
            if (workOrder != null)
            {
                if (workOrder.transportFee <= 0.0)
                {
                    levent.Level = LogLevel.Error;
                    levent.Message = "There is no transportation fee associated with this work order - there is no PayPal transaction required. WO#:" + workOrder.ID;
                    log.Log(levent);
                    return View("IndexError", workOrder);
                }
                else
                {
                    payment = workOrder.transportFee;
                }
            }
            else
            {
                levent.Level = LogLevel.Error;
                levent.Message = "WorkOrder ID not valid Work Order. WO#:" + workOrder.ID;
                log.Log(levent);
                return View("IndexError", workOrder);
            }

            // PayPal call to get buyer details
            PaypalExpressCheckout paypal = new PaypalExpressCheckout();
            GetExpressCheckoutDetailsResponseType detailsResponse = paypal.GetExpressCheckoutDetails(token);

            if (detailsResponse != null)
            {
                // # Success values
                if (detailsResponse.Ack.ToString().Trim().ToUpper().Equals("SUCCESS"))
                {
                    if ((detailsResponse.GetExpressCheckoutDetailsResponseDetails.PaymentDetails == null) ||
                        (Convert.ToDouble(detailsResponse.GetExpressCheckoutDetailsResponseDetails.PaymentDetails[0].OrderTotal.value) != workOrder.transportFee))
                    {
                        // PayPal charge is different than transportFee in database - can't process payment
                        levent.Level = LogLevel.Error;
                        levent.Message = "Transport Fee request to PayPal is a different amount than associated with the WO in database. WO# " + workOrder.ID;
                        log.Log(levent);
                        return View("IndexError", workOrder);
                    }

                    // Unique PayPal Customer Account identification number. This value will be null unless 
                    // you authorize the payment by redirecting to PayPal after `SetExpressCheckout` call.
                    workOrder.paypalPayerId = detailsResponse.GetExpressCheckoutDetailsResponseDetails.PayerInfo.PayerID;

                    woServ.Save(workOrder, userName);

                }
                // # Error Values
                else
                {
                    List<ErrorType> errorMessages = detailsResponse.Errors;
                    foreach (ErrorType error in errorMessages)
                    {
                        levent.Level = LogLevel.Error;
                        levent.Message += error.LongMessage;
                        log.Log(levent);

                        workOrder.paypalErrors += error.ShortMessage;
                    }

                    // Save work order updates
                    woServ.Save(workOrder, userName);

                    return View("IndexError", workOrder);
                }
            }
            else
            {
                levent.Level = LogLevel.Error;
                levent.Message = "The response from PayPal GetExpressCheckoutDetailsResponseType API was null. WO#:" + workOrder.ID;
                log.Log(levent);
                return View("IndexError", workOrder);
            }

            return View("IndexPostPaypal", workOrder);
            
        }

        /// <summary>
        /// GET: /HirerWorkOrder/PaymentCancel
        /// </summary>
        /// <param name="token">PayPal Token</param>
        /// <returns>MVC Action Result</returns>
        [Authorize(Roles = "Hirer")]
        public ActionResult PaymentCancel(string token, string orderID)
        {
            /*
            WorkOrder woAll;
            WorkOrder workOrder;
            if (string.IsNullOrEmpty(token) && orderID != null)
            {
                workOrder = woServ.Get(Convert.ToInt32(orderID));
            }
            else
            {
                woAll = woServ.GetRepo().GetAllQ().Where(wo => wo.paypalToken == token).FirstOrDefault();
                workOrder = woServ.Get(woAll.ID);
            }
            if (workOrder == null)
            {
                levent.Level = LogLevel.Error;
                levent.Message = "WorkOrder ID not valid Work Order. WO#:" + workOrder.ID;
                log.Log(levent);
                return View("IndexError", workOrder);
            }
            */
            return View("IndexCancel");
        }

        /// <summary>
        /// POST: /HirerWorkOrder/PaymentCancel
        /// </summary>
        /// <param name="token">PayPal Token</param>
        /// <returns>MVC Action Result</returns>
        [HttpPost]
        [Authorize(Roles = "Hirer")]
        public ActionResult PaymentCancel(string token2)
        {
            WorkOrder woAll = woServ.GetRepo().GetAllQ().Where(wo => wo.paypalToken == token2).FirstOrDefault();
            WorkOrder workOrder = woServ.Get(woAll.ID);
            if (workOrder == null)
            {
                levent.Level = LogLevel.Error;
                levent.Message = "WorkOrder ID not valid Work Order. WO#:" + workOrder.ID;
                log.Log(levent);
                return View("IndexError", workOrder);
            }

            return View("IndexCancel", workOrder);
        }

        /// <summary>
        /// GET: /HirerWorkOrder/PaymentComplete
        /// </summary>
        /// <param name="token">PayPal Token</param>
        /// <param name="payerId">PayPal Payer ID</param>
        /// <param name="userName">User performing action</param>
        /// <returns>MVC Action Result</returns>
        [UserNameFilter]
        [Authorize(Roles = "Hirer")]
        public ActionResult PaymentComplete(string userName)
        {
            /*
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(payerId)) 
            {
                return View("IndexCompleteEmpty");
            }

            WorkOrder woAll = woServ.GetRepo().GetAllQ().Where(wo => wo.paypalToken == token).FirstOrDefault();
            WorkOrder workOrder = woServ.Get(woAll.ID);
            if (workOrder != null)
            {
                if (workOrder.transportFee <= 0.0)
                {
                    levent.Level = LogLevel.Error;
                    levent.Message = "There is no transportation fee associated with this work order - there is no PayPal transaction required. WO#:" + workOrder.ID;
                    log.Log(levent);
                    return View("IndexError", workOrder);
                }
                else
                {
                    payment = workOrder.transportFee;
                }
            }
            else
            {
                levent.Level = LogLevel.Error;
                levent.Message = "WorkOrder ID not valid Work Order. WO#:" + workOrder.ID;
                log.Log(levent);
                return View("IndexError", workOrder);
            }
            */
            return View("IndexCompleteEmpty");
            /*
            // TODO: store payerID in WO table - not being stored for some reason?!
            double payment = 0.0;

            // TODO: There was an issue with the WO returned by the first query below - the work order
            // can't be saved unless the work order is retrieved with the woServ.Get() call
            WorkOrder woAll = woServ.GetRepo().GetAllQ().Where(wo => wo.paypalToken == token).FirstOrDefault();
            WorkOrder workOrder = woServ.Get(woAll.ID);
            if (workOrder != null)
            {
                if (workOrder.transportFee <= 0.0)
                {
                    levent.Level = LogLevel.Error;
                    levent.Message = "There is no transportation fee associated with this work order - there is no PayPal transaction required. WO#:" + workOrder.ID;
                    log.Log(levent);
                    return View("IndexError", workOrder);
                }
                else
                {
                    payment = workOrder.transportFee;
                }
            }
            else
            {
                levent.Level = LogLevel.Error;
                levent.Message = "WorkOrder ID not valid Work Order. WO#:" + workOrder.ID;
                log.Log(levent);
                return View("IndexError", workOrder);
            }

            if (workOrder.paypalTransactID != null)
            {
                // Error - an attempt is being made to make a second payment request on token
                levent.Level = LogLevel.Error;
                levent.Message = "An attempt is being made to make a second payment request on token. WO# " + workOrder.ID;
                log.Log(levent);
                return View("IndexError", workOrder);
            }
            else
            {
                // PayPal call to authorize payment
                PaypalExpressCheckout paypal = new PaypalExpressCheckout();
                if (paypal == null)
                {
                    // Error - an attempt is being made to make a second payment request on token
                    levent.Level = LogLevel.Error;
                    levent.Message = "PayPal ExpressCheckout call failed for: " + workOrder.ID;
                    log.Log(levent);
                    return View("IndexError", workOrder);
                }

                DoExpressCheckoutPaymentResponseType paymentResponse = paypal.DoExpressCheckoutPayment(token, payerId, payment.ToString());
                if (paymentResponse != null)
                {
                    // # Success values
                    if (paymentResponse.Ack != null && paymentResponse.Ack.ToString().Trim().ToUpper().Equals("SUCCESS"))
                    {
                        if (paymentResponse.DoExpressCheckoutPaymentResponseDetails == null)
                        {
                            levent.Level = LogLevel.Error;
                            levent.Message = "DoExpressCheckoutPaymentResponseDetails is null. WO# " + workOrder.ID;
                            log.Log(levent);
                            return View("IndexError", workOrder);
                        }

                        if (paymentResponse.DoExpressCheckoutPaymentResponseDetails.Token != token)
                        {
                            // Retrieved token doesn't match stored token
                            // Log error & continue - payment already made
                            levent.Level = LogLevel.Error;
                            levent.Message = "Retrieved token doesn't match stored token - payment accepted for token. WO# " + workOrder.ID + "Token# " + token;
                            log.Log(levent);
                        }

                        if (paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo != null)
                        {
                            // Note: Only one payment should be sent
                            if (paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo.Count != 1)
                            {
                                // Number of payments different from expected payments
                                // Log error & continue - payment already made
                                levent.Level = LogLevel.Error;
                                levent.Message = "Number of payments different from expected payments. WO# " + workOrder.ID + "Payment Count: " + paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo.Count;
                                log.Log(levent);
                            }

                            if (paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0] == null || 
                                paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0].GrossAmount == null || 
                                Convert.ToDouble(paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0].GrossAmount.value) != workOrder.transportFee)
                            {
                                // PayPal charge different from expected charge
                                // Log error & continue - payment already made
                                levent.Level = LogLevel.Error;
                                levent.Message = "PayPal charge different from expected charge. WO# " + workOrder.ID;
                                log.Log(levent);
                            }
                            else if (paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0].FeeAmount == null)
                            {
                                levent.Level = LogLevel.Error;
                                levent.Message = "PayPal fee not charged. WO# " + workOrder.ID;
                                log.Log(levent);
                            }
                            else
                            {
                                // Note: PayPal charges for online payment service
                                workOrder.paypalFee = Convert.ToDouble(paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0].FeeAmount.value);
                            }
                        }
                        else
                        {
                            // PayPal information not provided
                            // Log error & continue - payment already made
                            levent.Level = LogLevel.Error;
                            levent.Message = "PayPal return information not provided (internal error). WO# " + workOrder.ID;
                            log.Log(levent);
                        }

                        // TODO: This value is hard-coded in for PayPal - should do a lookup instead
                        workOrder.transportTransactType = 256;

                        if (paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0] != null)
                        {
                            // Store transactID in hidden field for comparisons
                            workOrder.paypalTransactID = paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0].TransactionID;

                            // Transaction identification number of the transaction that was created. This field is only 
                            // returned after a successful transaction for DoExpressCheckout has occurred.
                            // Note: truncated string to ensure it fits in database
                            workOrder.transportTransactID = ("ID: " + paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0].TransactionID +
                                "|| Date: " + paymentResponse.DoExpressCheckoutPaymentResponseDetails.PaymentInfo[0].PaymentDate + workOrder.transportTransactID).Substring(0, 50);
                        }

                        woServ.Save(workOrder, userName);

                        // Pass Data to view
                        ViewBag.payment = "$" + workOrder.transportFee.ToString();
                    }
                    // # Error Values
                    else
                    {
                        List<ErrorType> errorMessages = paymentResponse.Errors;
                        if (errorMessages != null)
                        {
                            foreach (ErrorType error in errorMessages)
                            {
                                levent.Level = LogLevel.Error;
                                levent.Message += error.LongMessage;
                                log.Log(levent);

                                workOrder.paypalErrors += error.ShortMessage;
                            }

                            // Save work order updates
                            woServ.Save(workOrder, userName);
                        }
                        else
                        {
                            levent.Level = LogLevel.Error;
                            levent.Message = "Retrieved error list is null for token. WO# " + workOrder.ID + "Token# " + token;
                            log.Log(levent);
                        }
                        return View("IndexError", workOrder);
                    }
                }
                else
                {
                    levent.Level = LogLevel.Error;
                    levent.Message = "The response from PayPal GetExpressCheckoutDetailsResponseType API was null. WO#:" + workOrder.ID;
                    log.Log(levent);
                    return View("IndexError", workOrder);
                }
            }
            */
        }

        #endregion
    }
}
