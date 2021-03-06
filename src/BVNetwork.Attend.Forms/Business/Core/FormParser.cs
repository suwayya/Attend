﻿using BVNetwork.Attend.Business.API;
using BVNetwork.Attend.Business.Participant;
using BVNetwork.Attend.Business.Text;
using BVNetwork.Attend.Forms.Models.Forms;
using BVNetwork.Attend.Models.Pages;
using EPiServer;
using EPiServer.Core;
using EPiServer.Forms.Core.Models;
using EPiServer.Forms.Implementation.Elements;
using EPiServer.ServiceLocation;
using EPiServer.XForms;
using EPiServer.XForms.Util;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.Linq;

namespace BVNetwork.Attend.Forms.Business.Core
{
    public class FormParser
    {
        private const string __AttendEvent = "__AttendEvent";
        private const string __AttendEmail = "__AttendEmail";
        private const string __AttendSessions = "__AttendSessions";
        private const string __AttendParticipantEmail = "__AttendParticipantEmail";
        private const string __AttendParticipantCode = "__AttendParticipantCode";


        public static void ProcessForm(NameValueCollection rawFormData, FormContainerBlock formBlock, Submission submissionData) {

            string eventPageIds = rawFormData[__AttendEvent];
            string participantEmail = rawFormData[__AttendParticipantEmail];
            string participantCode = rawFormData[__AttendParticipantCode];
            List<string> eventPages = null;
            if (string.IsNullOrEmpty(eventPageIds)) // Not an Attend form - exit form processing.
                return;
            if (eventPageIds.Split(',').Length > 1)
                eventPages = eventPageIds.Split(',').ToList<string>();
            else
                eventPages = new List<string>() { eventPageIds };

            SetPrivatePropertyValue<PropertyData>(false, "IsReadOnly", formBlock.Property["SubmitSuccessMessage"]);

            NameValueCollection nvc = FormParser.ParseForm(submissionData, formBlock);
            StringBuilder message = new StringBuilder();
            StringBuilder codes = new StringBuilder();
            foreach (string eventPageId in eventPages) { 
                ContentReference eventPage = new ContentReference(eventPageId).ToPageReference();
                EventPageBase eventPageBase = ServiceLocator.Current.GetInstance<IContentRepository>().Get<EventPageBase>(eventPage);

                if (eventPages.Count > 1)
                    message.Append("<strong>" + eventPageBase.Name + "</strong><br/>");

                IParticipant participant = null;
                if (!string.IsNullOrEmpty(participantCode) && !string.IsNullOrEmpty(participantEmail))
                {
                    participant = BVNetwork.Attend.Business.API.AttendRegistrationEngine.GetParticipant(participantEmail, participantCode);
                    participant = FormParser.UpdateParticipation(participant, nvc);
                }
                if (participant == null)
                    participant = FormParser.GenerateParticipation(eventPage, nvc);


                if (participant.AttendStatus == AttendStatus.Confirmed.ToString())
                {
                    if (eventPageBase.CompleteContentXhtml != null)
                        message.Append(eventPageBase.CompleteContentXhtml.ToHtmlString());
                    else
                        message.Append(EPiServer.Framework.Localization.LocalizationService.Current.GetString("/eventRegistrationPage/confirmed"));
                }

                if (participant.AttendStatus == AttendStatus.Submitted.ToString())
                    if (eventPageBase.SubmittedContentXhtml != null)
                        message.Append(eventPageBase.SubmittedContentXhtml.ToHtmlString());
                    else
                        message.Append(EPiServer.Framework.Localization.LocalizationService.Current.GetString("/eventRegistrationPage/submitted"));
                if (message.Length == 0)
                    message.Append(EPiServer.Framework.Localization.LocalizationService.Current.GetString("/eventRegistrationPage/error"));
                message.Append("<br/><br/>");
                codes.Append(participant.Code + ",");
            }

            if (formBlock.RedirectToPage != null) {
                SetPrivatePropertyValue<PropertyData>(false, "IsReadOnly", formBlock.Property["RedirectToPage"]);
                Url redirectUrl = new Url(formBlock.RedirectToPage.Uri.ToString()+"?code="+codes.ToString()+"&eventPageID="+eventPageIds);
                formBlock.RedirectToPage = redirectUrl;
            }
            formBlock.SubmitSuccessMessage = new XhtmlString(message.ToString());

        }


        public static NameValueCollection GetFormData(IParticipant participant) {
            NameValueCollection _formControls = new NameValueCollection();
            return _formControls;
        }


        public static string SerializeForm(NameValueCollection values) {
            var allValues = new XElement("FormData",
                                        values.AllKeys.Select(o => new XElement(o.Replace(" ", "_"), values[o]))
                                     );
            var result = new XElement("FormData", (from element in allValues.Elements() where element.Name.ToString().StartsWith("__") == false select element));
            return result.ToString();
        }

        public static NameValueCollection ParseForm(Submission submission, FormContainerBlock formContainer) {
            NameValueCollection formData = new NameValueCollection();
            IContentRepository rep = EPiServer.ServiceLocation.ServiceLocator.Current.GetInstance<IContentRepository>();
            string email = string.Empty;
            foreach (var element in formContainer.ElementsArea.Items)
            {
                bool skip = false;
                var control = rep.Get<IContent>(element.ContentLink);
                string value = string.Empty;
                string key = "__field_" + control.ContentLink.ID.ToString();
                if(submission.Data.ContainsKey(key)) {
                    var elementObject = submission.Data[key];
                    if (elementObject != null) { 
                        value = elementObject.ToString();
                    if (new [] { "email", "e-mail", "epost", "e-post" }.Contains(control.Name.ToLower() ))
                        { 
                        formData.Add(__AttendEmail, value);
                        skip = true;
                    }
                    if (control as AttendSessionForm != null) { 
                            formData.Add(__AttendSessions, value);
                            skip = true;
                        }
                    }
                    if(!skip)
                    formData.Add(control.Name, value);
                }
            }
            return formData;

        }

        

        public static IParticipant GenerateParticipation(ContentReference eventPage, NameValueCollection nvc) {
            string email = nvc.AllKeys.Contains(__AttendEmail) ? nvc[__AttendEmail] : "";
            IParticipant participant = null;
            if (!string.IsNullOrEmpty(email)) { 
                participant = Attend.Business.API.AttendRegistrationEngine.GenerateParticipation(eventPage, email, FormParser.SerializeForm(nvc));
                string sessions = nvc.AllKeys.Contains(__AttendSessions) ? nvc[__AttendSessions] : "";
                participant.Sessions = parseSessionsToContentArea(parseSessionsToStringArray(sessions));
            }
            Attend.Business.API.AttendRegistrationEngine.SaveParticipant(participant);
            Attend.Business.API.AttendRegistrationEngine.SendStatusMail(participant);

            return participant;
        }


        public static IParticipant UpdateParticipation(IParticipant participant, NameValueCollection nvc)
        {
            string sessions = nvc.AllKeys.Contains(__AttendSessions) ? nvc[__AttendSessions] : "";
            participant.Sessions = parseSessionsToContentArea(parseSessionsToStringArray(sessions));
            participant.XForm = FormParser.SerializeForm(nvc);
            Attend.Business.API.AttendRegistrationEngine.SaveParticipant(participant);

            return participant;
        }


        private static string[] parseSessionsToStringArray(string sessions) {
            string[] result = sessions.Replace(" ","").Split(',');
            result = result.Where(x => !string.IsNullOrEmpty(x)).ToArray();
            return result;
        }

        private static ContentArea parseSessionsToContentArea(string[] sessions) {
            var sessionsContentArea = new ContentArea();
            foreach (var session in sessions)
            {
                var sessionContentReference = new ContentReference(session);
                sessionsContentArea.Items.Add(new ContentAreaItem() { ContentLink = sessionContentReference });
            }
            return sessionsContentArea;
        }


        public static void SetPrivatePropertyValue<T>(object obj, string propName, T val)
        {
            Type t = val.GetType();
            if (t.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) == null)
                throw new ArgumentOutOfRangeException("propName", string.Format("Property {0} was not found in Type {1}", propName, obj.GetType().FullName));
            t.InvokeMember(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.SetProperty | BindingFlags.Instance, null, val, new object[] { obj });
        }



        public static string GetEmail(NameValueCollection formData) {
            if (formData["__AttendEmail"] != null)
                return formData["__AttendEmail"];
            return string.Empty;
        }


    }
}