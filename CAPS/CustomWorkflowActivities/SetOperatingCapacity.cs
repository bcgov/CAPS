﻿using CAPS.DataContext;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomWorkflowActivities
{
    /// <summary>
    /// Updates the operating capacity on all active facilities and their capacity reporting records.
    /// </summary>
    public class SetOperatingCapacity : CodeActivity
    {
        [Output("Error")]
        public OutArgument<bool> error { get; set; }

        [Output("ErrorMessage")]
        public OutArgument<string> errorMessage { get; set; }

        protected override void Execute(CodeActivityContext executionContext)
        {
            ITracingService tracingService = executionContext.GetExtension<ITracingService>();

            IWorkflowContext context = executionContext.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = executionContext.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            tracingService.Trace("{0}{1}", "Start Custom Workflow Activity: SetOperatingCapacity", DateTime.Now.ToLongTimeString());

            //Update to run on school district
            var recordId = context.PrimaryEntityId;

            try
            {
                //Get Global Capacity values
                var capacity = new Services.CapacityFactors(service);

                Services.OperatingCapacity capacityService = new Services.OperatingCapacity(service, tracingService, capacity);

                tracingService.Trace("Section: {0}", "Update Facilities");
                #region Update Facilities
                //get Facilities
                var fetchXML = "<fetch version=\"1.0\" output-format=\"xml-platform\" mapping=\"logical\" distinct=\"false\" >" +
                           "<entity name=\"caps_facility\">" +
                                "<attribute name=\"caps_facilityid\" /> " +
                                "<attribute name=\"caps_name\" /> " +
                                "<attribute name=\"caps_lowestgrade\" /> " +
                                "<attribute name=\"caps_highestgrade\" /> " +
                                "<attribute name=\"caps_adjusteddesigncapacitysecondary\" /> " +
                                "<attribute name=\"caps_adjusteddesigncapacitykindergarten\" /> " +
                                "<attribute name=\"caps_adjusteddesigncapacityelementary\" /> " +
                                "<order attribute=\"caps_name\" descending=\"false\" /> " +
                                    "<filter type=\"and\" > " +
                                           "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /> " +
                                           "<condition attribute=\"caps_lowestgrade\" operator=\"not-null\" />"+
                                           "<condition attribute=\"caps_highestgrade\" operator=\"not-null\" />" +
                                           "<condition attribute=\"caps_schooldistrict\" operator=\"eq\" value=\"{" + recordId + "}\" />" +
                                       "</filter> " +
                                "<link-entity name=\"caps_facilitytype\" from=\"caps_facilitytypeid\" to=\"caps_currentfacilitytype\" link-type=\"inner\" alias=\"ac\" > " +
                                        "<filter type=\"and\" > " +
                                            "<condition attribute=\"caps_schooltype\" operator=\"not-null\" /> " +
                                        "</filter> " +
                                "</link-entity> " +
                            "</entity> " +
                            "</fetch>";


                EntityCollection results = service.RetrieveMultiple(new FetchExpression(fetchXML));

                foreach (caps_Facility facilityRecord in results.Entities)
                {
                    tracingService.Trace("Facility:{0}", facilityRecord.caps_Name);

                    var kDesign = facilityRecord.caps_AdjustedDesignCapacityKindergarten.GetValueOrDefault(0);
                    var eDesign = facilityRecord.caps_AdjustedDesignCapacityElementary.GetValueOrDefault(0);
                    var sDesign = facilityRecord.caps_AdjustedDesignCapacitySecondary.GetValueOrDefault(0);
                    var lowestGrade = facilityRecord.caps_LowestGrade.Value;
                    var highestGrade = facilityRecord.caps_HighestGrade.Value;

                    var result = capacityService.Calculate(kDesign, eDesign, sDesign, lowestGrade, highestGrade);

                    //Update Facility
                    var recordToUpdate = new caps_Facility();
                    recordToUpdate.Id = facilityRecord.Id;
                    recordToUpdate.caps_OperatingCapacityKindergarten = result.KindergartenCapacity;
                    recordToUpdate.caps_OperatingCapacityElementary = result.ElementaryCapacity;
                    recordToUpdate.caps_OperatingCapacitySecondary = result.SecondaryCapacity;
                    service.Update(recordToUpdate);
                }
                #endregion

                tracingService.Trace("Section: {0}", "Update Capacity Reporting");
                #region Update Capacity Reporting
                //Update Capacity Reporting
                var capacityFetchXML = "<fetch version=\"1.0\" output-format=\"xml-platform\" mapping=\"logical\" distinct=\"false\" >" +
                                       "<entity name=\"caps_capacityreporting\" > " +
                                        "<attribute name=\"caps_capacityreportingid\" /> " +
                                        "<attribute name=\"caps_secondary_designcapacity\" /> " +
                                        "<attribute name=\"caps_kindergarten_designcapacity\" /> " +
                                        "<attribute name=\"caps_elementary_designcapacity\" /> " +
                                         "<order attribute=\"caps_secondary_designutilization\" descending=\"false\" /> " +
                                            "<link-entity name=\"caps_facility\" from=\"caps_facilityid\" to=\"caps_facility\" visible=\"false\" link-type=\"inner\" alias=\"facility\" > " +
                                                "<attribute name=\"caps_lowestgrade\" /> " +
                                                "<attribute name=\"caps_highestgrade\" /> " +
                                                "<filter type=\"and\" > " +
                                                    "<condition attribute=\"caps_schooldistrict\" operator=\"eq\" value=\"{" + recordId + "}\" />" +
                                                    "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /> " +
                                                    "<condition attribute=\"caps_lowestgrade\" operator=\"not-null\" />" +
                                                    "<condition attribute=\"caps_highestgrade\" operator=\"not-null\" />" +
                                                "</filter> " +
                                            "</link-entity> " +
                                                "<link-entity name=\"edu_year\" from=\"edu_yearid\" to=\"caps_schoolyear\" link-type=\"inner\" alias=\"ab\" >" +
                                                "<filter type=\"and\"> " +
                                                    "<condition attribute=\"statuscode\" operator=\"in\">" +
                                                       "<value>1</value> " +
                                                       "<value>757500000</value> " +
                                                     "</condition>" +
                                                   "</filter>" +
                                                 "</link-entity>" +
                                            "</entity> " +
                                     "</fetch> ";

                tracingService.Trace("Capacity Reporting Fetch: {0}", capacityFetchXML);

                EntityCollection capacityResults = service.RetrieveMultiple(new FetchExpression(capacityFetchXML));

                foreach (caps_CapacityReporting capacityRecord in capacityResults.Entities)
                {
                    var kDesign = capacityRecord.caps_Kindergarten_designcapacity.GetValueOrDefault(0);
                    var eDesign = capacityRecord.caps_Elementary_designcapacity.GetValueOrDefault(0);
                    var sDesign = capacityRecord.caps_Secondary_designcapacity.GetValueOrDefault(0);

                    tracingService.Trace("Capacity Reporting: {0}", capacityRecord.Id);
                    tracingService.Trace("Lowest: {0}", ((AliasedValue)capacityRecord["facility.caps_lowestgrade"]).Value);
                    tracingService.Trace("Highest: {0}", ((AliasedValue)capacityRecord["facility.caps_highestgrade"]).Value);

                    var lowestGrade = ((OptionSetValue)((AliasedValue)capacityRecord["facility.caps_lowestgrade"]).Value).Value;
                    var highestGrade = ((OptionSetValue)((AliasedValue)capacityRecord["facility.caps_highestgrade"]).Value).Value;

                    tracingService.Trace("Start Calculate: {0}", capacityRecord.Id);
                    var result = capacityService.Calculate(kDesign, eDesign, sDesign, lowestGrade, highestGrade);
                    tracingService.Trace("End Calculate: {0}", capacityRecord.Id);

                    //Update Capacity Reporting
                    var recordToUpdate = new caps_CapacityReporting();
                    recordToUpdate.Id = capacityRecord.Id;
                    recordToUpdate.caps_Kindergarten_operatingcapacity = result.KindergartenCapacity;
                    recordToUpdate.caps_Elementary_operatingcapacity = result.ElementaryCapacity;
                    recordToUpdate.caps_Secondary_operatingcapacity = result.SecondaryCapacity;
                    service.Update(recordToUpdate);
                }
                #endregion

                tracingService.Trace("Section: {0}", "Update Draft Project Requests");
                #region Update Draft Project Requests
                //Update Draft Project Requests
                var projectRequestFetchXML = "<fetch version=\"1.0\" output-format=\"xml-platform\" mapping=\"logical\" distinct=\"false\">" +
                                            "<entity name=\"caps_project\" > " +
                                               "<attribute name=\"caps_facility\" /> " +
                                                "<attribute name=\"caps_changeindesigncapacitykindergarten\" /> " +
                                                 "<attribute name=\"caps_changeindesigncapacityelementary\" /> " +
                                                  "<attribute name=\"caps_changeindesigncapacitysecondary\" /> " +
                                                  "<attribute name=\"caps_futurelowestgrade\" /> " +
                                                  "<attribute name=\"caps_futurehighestgrade\" /> " +
                                                    "<order attribute=\"caps_projectcode\" descending=\"false\" /> " +
                                                       "<filter type=\"and\" > " +
                                                          "<condition attribute=\"statuscode\" operator=\"eq\" value=\"1\" /> " +
                                                          "<condition attribute=\"caps_schooldistrict\" operator=\"eq\" value=\"{" + recordId + "}\" />" +
                                                              "<filter type=\"or\" > " +
                                                                 "<condition attribute=\"caps_changeinoperatingcapacitykindergarten\" operator=\"not-null\" /> " +
                                                                    "<condition attribute=\"caps_changeinoperatingcapacityelementary\" operator=\"not-null\" /> " +
                                                                       "<condition attribute=\"caps_changeinoperatingcapacitysecondary\" operator=\"not-null\" /> " +
                                                                        "</filter> " +
                                                                      "</filter> " +
                                                                    "</entity> " +
                                                                  "</fetch> ";

                EntityCollection projectResults = service.RetrieveMultiple(new FetchExpression(projectRequestFetchXML));

                foreach (caps_Project projectRecord in projectResults.Entities)
                {
                    if (projectRecord.caps_FutureLowestGrade != null && projectRecord.caps_FutureHighestGrade != null)
                    {
                        var startingDesign_K = 0;
                        var startingDesign_E = 0;
                        var startingDesign_S = 0;

                        //if facility exists, then retrieve it
                        if (projectRecord.caps_Facility != null && projectRecord.caps_Facility.Id != Guid.Empty)
                        {
                            var facility = service.Retrieve(caps_Facility.EntityLogicalName, projectRecord.caps_Facility.Id, new ColumnSet("caps_adjusteddesigncapacitykindergarten", "caps_adjusteddesigncapacityelementary", "caps_adjusteddesigncapacitysecondary")) as caps_Facility;

                            if (facility != null)
                            {
                                startingDesign_K = facility.caps_AdjustedDesignCapacityKindergarten.GetValueOrDefault(0);
                                startingDesign_E = facility.caps_AdjustedDesignCapacityElementary.GetValueOrDefault(0);
                                startingDesign_S = facility.caps_AdjustedDesignCapacitySecondary.GetValueOrDefault(0);
                            }
                        }

                        var changeInDesign_K = startingDesign_K + projectRecord.caps_ChangeinDesignCapacityKindergarten.GetValueOrDefault(0);
                        var changeInDesign_E = startingDesign_E + projectRecord.caps_ChangeinDesignCapacityElementary.GetValueOrDefault(0);
                        var changeInDesign_S = startingDesign_S + projectRecord.caps_ChangeinDesignCapacitySecondary.GetValueOrDefault(0);

                        var result = capacityService.Calculate(changeInDesign_K, changeInDesign_E, changeInDesign_S, projectRecord.caps_FutureLowestGrade.Value, projectRecord.caps_FutureHighestGrade.Value);

                        var recordToUpdate = new caps_Project();
                        recordToUpdate.Id = projectRecord.Id;
                        recordToUpdate.caps_ChangeinOperatingCapacityKindergarten = Convert.ToInt32(result.KindergartenCapacity);
                        recordToUpdate.caps_ChangeinOperatingCapacityElementary = Convert.ToInt32(result.ElementaryCapacity);
                        recordToUpdate.caps_ChangeinOperatingCapacitySecondary = Convert.ToInt32(result.SecondaryCapacity);
                        service.Update(recordToUpdate);
                    }
                    else
                    {
                        //blank out operating capacity
                        var recordToUpdate = new caps_Project();
                        recordToUpdate.Id = projectRecord.Id;
                        recordToUpdate.caps_ChangeinOperatingCapacityKindergarten = null;
                        recordToUpdate.caps_ChangeinOperatingCapacityElementary = null;
                        recordToUpdate.caps_ChangeinOperatingCapacitySecondary = null;
                        service.Update(recordToUpdate);
                    }
                } 
                #endregion
    

                this.error.Set(executionContext, false);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Error Details: {0}", ex.Message);
                //might want to also include error message
                this.error.Set(executionContext, true);
                this.errorMessage.Set(executionContext, ex.Message);
            }

            tracingService.Trace("{0}{1}", "End Custom Workflow Activity: SetOperatingCapacity", DateTime.Now.ToLongTimeString());
        }

        private decimal GetBudgetCalculationValue(IOrganizationService service, string name)
        {

            FilterExpression filterName = new FilterExpression();
            filterName.Conditions.Add(new ConditionExpression("caps_name", ConditionOperator.Equal, name));

            QueryExpression query = new QueryExpression("caps_budgetcalc_value");
            query.ColumnSet.AddColumns("caps_value");
            query.Criteria.AddFilter(filterName);

            EntityCollection results = service.RetrieveMultiple(query);

            if (results.Entities.Count != 1) throw new Exception("Missing Budget Calculation Value: " + name);

            return results.Entities[0].GetAttributeValue<decimal>("caps_value");
        }
    }
}
