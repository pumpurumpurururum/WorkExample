using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCompany.Api.Base.Responses;
using MyCompany.Api.Services.AeroExpress;
using MyCompany.Api.Services.Common;
using MyCompany.Api.Services.SuppData;
using MyCompany.Api.Services.Transfer;
using MyCompany.ObjectModel.Services.SuppData;
using MyCompany.Core.Interfaces.ExternalServices.Dictionaries;
using MyCompany.Infrastructure.Extensions;
using MyCompany.BLogic.Utils.Extensions;
using NLog;
using System;
using MyCompany.Core.Interfaces.Static;

namespace MyCompany.Core.BLogic.Composing
{
    ///<summary>
    /// Класс которые объединяет разные варианты в один, иногда убирая дубли.
    ///</summary>
    public class ComposeHelper : IComposeHelper
    {
        private readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        private readonly IDictionarySuppData _dictionarySuppData;

        private readonly TimeSpan ostrovokStandartCheckInTime = new TimeSpan(14, 00, 00);
        private readonly TimeSpan ostrovokStandartCheckOutTime = new TimeSpan(12, 00, 00);

        public ComposeHelper(IDictionarySuppData dictionarySuppData)
        {
            _dictionarySuppData = dictionarySuppData;
        }

        /// <summary>
        /// Объединение результатов от нескольких поставщиков.
        /// </summary>
        /// <param name="responses">Ответы</param>
        /// <returns>Объединенный результат</returns>
        public SuppData_SearchResponse SuppData_ComposeSuppDataSearchResults(List<SuppData_SearchResponse> responses)
        {
            if (responses == null || responses.Count == 0)
            {
                return new SuppData_SearchResponse().AddMessageErrorInternal("1|Резутатов не вернулось");
            }

            if (responses.Count == 1)
            {
                var frsp = responses.First();
                if (frsp.SuppDataAvaibilities != null && frsp.SuppDataAvaibilities.Any())
                    foreach (var SuppDataAvaibility in frsp.SuppDataAvaibilities)
                        SuppDataAvaibility.UpdateSupplierList();
                return frsp;
            }

            var ret = new SuppData_SearchResponse();

            #region *** Обьединение сообщений об ошибках ***
            var errorResponses = responses.Where(n => !string.IsNullOrEmpty(n.ErrorMessage)).ToList();
            if (errorResponses.Count > 0)
            {
                foreach (var errorResponse in errorResponses)
                    ret.ErrorMessage = string.IsNullOrEmpty(ret.ErrorMessage) ? errorResponse.ErrorMessage : ret.ErrorMessage + "\r\n" + errorResponse.ErrorMessage;
            }
            ret.Success = responses.Any(n => n.Success);
            #endregion

            //Пока просто объединим ответы
            ret.SuppDataAvaibilities = new List<SuppData_SearchAvaibility>();
            foreach (SuppData_SearchResponse response in responses.Where(r => r.SuppDataAvaibilities != null))
            {
                ret.SuppDataAvaibilities.AddRange(response.SuppDataAvaibilities.Where(n => n != null));

                ret.SuppDataAvaibilities.ForEach(n =>
                {
                    n.SuppData.DescriptionEn = null;
                    n.SuppData.DescriptionRu = null;
                });
            }

            //группировка по гостиницам.
            var groups = ret.SuppDataAvaibilities.GroupBy(n => n.SuppData.Id);
            var lstDeleteSuppDatas = new List<SuppData_SearchAvaibility>();
            foreach (var group in groups)
            {
                var hh = group.First();
                hh.MinPrices = group.Where(z => z.MinPrices != null).SelectMany(z => z.MinPrices).ToList();
                hh.Rooms = group.Where(z => z.Rooms != null).SelectMany(z => z.Rooms).Where(z => z != null).OrderBy(z => z.TotalPrice?.Amount ?? 0).ToList();
                hh.Rooms.ForEach(r => r.Name = r.Name.StripTags());
                hh.UpdateSupplierList();

                //и остальные к ней.
                lstDeleteSuppDatas.AddRange(@group.Skip(1));
            }

            //Удалим то что объединили.
            foreach (var SuppData in lstDeleteSuppDatas)
                ret.SuppDataAvaibilities.Remove(SuppData);

            return ret;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="responses"></param>
        /// <returns></returns>
        public SuppData_PricingResponse SuppData_ComposeSuppDataPricingResults(List<SuppData_PricingResponse> responses)
        {
            if (responses == null || responses.Count == 0)
            {
                return new SuppData_PricingResponse { ErrorMessage = "1|Результатов не вернулось", Success = false };
            }

            if (responses.Count == 1)
            {
                return responses.First();
            }

            var response = new SuppData_PricingResponse();
            response.AddMessages(responses.Where(r => r.Messages != null).SelectMany(e => e.Messages));

            response.Success = responses.Any(n => n.SuppDataAvaibility != null && !n.HasErrors);

            var successResponses = responses.Where(n => n.SuppDataAvaibility != null).ToArray();
            if (!successResponses.Any())
            {
                return response;
            }

            var firstResponse = successResponses.First();
            responses.Remove(firstResponse);

            var rates = new List<SuppData_PricingRoom>();
            if (firstResponse.SuppDataAvaibility.Rooms != null)
            {
                rates.AddRange(firstResponse.SuppDataAvaibility.Rooms);
            }

            var firstDirectContractDetailsList = firstResponse.SuppDataAvaibility.AddiditionalInfo.DirectContractDetailsList;
            foreach (var pricingResponse in responses)
            {
                var responseDirectContractDetails = pricingResponse.SuppDataAvaibility?.AddiditionalInfo?.DirectContractDetailsList;
                if (responseDirectContractDetails != null && responseDirectContractDetails.Any())
                {
                    foreach (var contractDetail in responseDirectContractDetails)
                    {
                        if (!firstDirectContractDetailsList.Contains(contractDetail))
                        {
                            firstDirectContractDetailsList.Add(contractDetail);
                        }
                    }
                }
                if (pricingResponse?.SuppDataAvaibility?.Rooms != null)
                {
                    rates.AddRange(pricingResponse.SuppDataAvaibility.Rooms);
                }
            }

            firstResponse.SuppDataAvaibility.Rooms = rates.OrderBy(n => n.TotalPrice?.Amount ?? 0).ToList();

            response.SuppDataAvaibility = firstResponse.SuppDataAvaibility;

            return response;
        }

        /// <summary>
        /// Объединяем все бронирования.
        /// </summary>
        /// <param name="responses"></param>
        /// <returns></returns>
        public Common_BookingInfoResponse Common_ComposeBookingInfoResults(List<Common_BookingInfoResponse> responses)
        {
            var ret = new Common_BookingInfoResponse();
            if (responses == null || responses.Count == 0)
            {
                ret.AddMessage("1|Результатов не вернулось", ResponseMessageType.Error, ResponseMessageSource.Internal);
                return ret;
            }

            if (responses.Count == 1)
            {
                ret = responses.First();
                return ret;
            }

            #region *** Обьединение сообщений об ошибках ***
            var errorResponses = responses.Where(n => !string.IsNullOrEmpty(n.ErrorMessage)).ToList();
            if (errorResponses.Count > 0)
            {
                foreach (var errorResponse in errorResponses)
                {
                    ret.ErrorMessage = string.IsNullOrEmpty(ret.ErrorMessage) ? errorResponse.ErrorMessage : ret.ErrorMessage + "\r\n" + errorResponse.ErrorMessage;
                }
                ret.Success = false;
            }
            else
            {
                ret.Success = true;
            }
            #endregion

            //объединим все бронирования.
            foreach (var bookingInfo in responses)
            {
                foreach (var responseItem in bookingInfo.Responses)
                {
                    ret.Responses.Add(responseItem);
                }
            }

            return ret;
        }
        
        /// <summary>
        /// Объединяем все ответы.
        /// </summary>
        /// <param name="responses"></param>
        /// <returns></returns>
        public Transfer_GetTripDocumentResponse Transfer_GenerateDocumentResults(List<Transfer_GetTripDocumentResponse> responses)
        {
            if (responses == null || responses.Count == 0)
            {
                return new Transfer_GetTripDocumentResponse().AddMessageErrorInternal("1|Результатов не вернулось");
            }

            if (responses.Count == 1)
            {
                return responses.First();
            }

            var response = new Transfer_GetTripDocumentResponse();

            var messages = responses.Where(n => n.Messages != null && n.Messages.Any()).SelectMany(n => n.Messages).ToArray();
            response.AddMessages(messages);

            response.Success = !response.HasErrors;

            response.BlobFingerprints = responses.Where(n => n.BlobFingerprints != null)
                                                 .SelectMany(n => n.BlobFingerprints)
                                                 .ToList();

            return response;
        }

        /// <summary>
        /// Объединение результатов аэроэкспресса
        /// </summary>
        /// <param name="responses"></param>
        /// <returns></returns>
        public AeroExpress_PricingResponse AeroExpress_ComposeSuppDataPricingResults(List<AeroExpress_PricingResponse> responses)
        {
            var ret = new AeroExpress_PricingResponse();
            if (responses == null || responses.Count == 0)
            {
                return new AeroExpress_PricingResponse { ErrorMessage = "1|Резутатов не вернулось", Success = false };
            }
            if (responses.Count == 1)
            {
                ret = responses.First();
                return ret;
            }

            #region *** Обьединение сообщений об ошибках ***
            var errorResponses = responses.Where(n => !string.IsNullOrEmpty(n.ErrorMessage)).ToList();
            if (errorResponses.Count > 0)
            {
                foreach (var errorResponse in errorResponses)
                {
                    ret.ErrorMessage = string.IsNullOrEmpty(ret.ErrorMessage) ? errorResponse.ErrorMessage : ret.ErrorMessage + "\r\n" + errorResponse.ErrorMessage;
                }
                ret.Success = false;
            }
            else
            {
                ret.Success = true;
            }
            #endregion

            var okresp = responses.Where(n => n.PricingItems != null);
            if (!okresp.Any())
            {
                return ret;
            }

            var firstresp = okresp.First();
            responses.Remove(firstresp);

            if (firstresp.PricingItems == null) { firstresp.PricingItems = new List<AeroExpress_PricingResponseItem>(); }
            foreach (var uSuppDataPricingResponse in responses)
            {
                if (uSuppDataPricingResponse.PricingItems != null)
                {
                    firstresp.PricingItems.AddRange(uSuppDataPricingResponse.PricingItems);
                }
            }
            ret.PricingItems = firstresp.PricingItems;
            return ret;
        }

        public void SuppData_FillSuppDataInfo(SuppData_BookingInfoResponse[] responses)
        {
            var rooms = responses.Where(r => r.Booking?.Rooms != null).SelectMany(r => r.Booking.Rooms).ToArray();
            var SuppDataIds = rooms.Select(r => r.SuppData.Id).Distinct().ToArray();
            var SuppDataInfos = new ConcurrentDictionary<int, SuppData_ShortInfo>();
            Parallel.ForEach(SuppDataIds, id =>
            {
                var SuppDataInfo = _dictionarySuppData.GetSuppData(id);
                SuppDataInfos.TryAdd(id, SuppDataInfo);
            });

            foreach (var room in rooms)
            {
                room.SuppData = SuppDataInfos[room.SuppData.Id];
            }
        }

        public void SuppData_FillSuppDataInfo(SuppData_SearchResponse SuppDataSearchResponse)
        {
            const int count = 1000;

            var totalCount = SuppDataSearchResponse.SuppDataAvaibilities?.Count;
            if (totalCount == null || totalCount == 0)
            {
                SuppDataSearchResponse.AddMessageErrorInternal("SuppDataAvaibilities null! Отелей не вернулось от поставщиков.");
                return;
            }
                
            var totalPages = totalCount / count;

            var SuppDataIdPages = new List<int[]>();
            for (var pageCount = 0; pageCount <= totalPages; pageCount++)
            {
                var item = SuppDataSearchResponse.SuppDataAvaibilities?.Select(z => z.SuppData.Id).Skip(pageCount * count).Take(count).ToArray();
                SuppDataIdPages.Add(item);
            }

            var concurrentBag = new ConcurrentBag<SuppData_ShortInfo[]>();
            Parallel.ForEach(SuppDataIdPages, currentPage =>
            {
                var result = _dictionarySuppData.GetSuppDatas(currentPage);
                if (result != null) concurrentBag.Add(result);
            });

            var plainList = concurrentBag.SelectMany(s => s).ToArray();

            foreach (var SuppData in SuppDataSearchResponse.SuppDataAvaibilities)
            {
                if (SuppData?.SuppData != null)
                {
                    var currentId = SuppData.SuppData.Id;
                    var currentInfo = plainList.FirstOrDefault(f => f.Id == currentId);
                    if (currentInfo != null)
                    {
                        SuppData.SuppData = currentInfo;
                    }
                    else
                    {
                        _logger.Error($"Cannot get SuppDataInfo by SuppDataid {currentId}");
                    }

                    //Заполним также SuppData в minPrices. Необходимо для BG-2960
                    foreach (var minPrice in SuppData.MinPrices)
                    {
                        minPrice.SuppDataId = SuppData.SuppData.Id;
                    }
                }               
            }
        }

        public void SuppData_FillSuppDataInfo(SuppData_PricingResponse pricingResponse)
        {
            var SuppDataId = pricingResponse.SuppDataAvaibility?.SuppData.Id;
            if (SuppDataId == null)
            {
                pricingResponse.AddMessageErrorInternal("SuppDataId is null!");
                return;
            }
            var SuppDataInfo = _dictionarySuppData.GetSuppData(SuppDataId.Value);
            if (SuppDataInfo == null)
            {
                pricingResponse.AddMessageErrorInternal("Ошибка получения информации из Dictionaries.");
                return;
            }

            pricingResponse.SuppDataAvaibility.SuppData = SuppDataInfo;

            foreach (var room in pricingResponse.SuppDataAvaibility.Rooms)
            {
                FillImportantInformation(room, SuppDataInfo);
                FillArrivalAndDepartureTime(room, SuppDataInfo);
            }
        }

        public void SuppData_FillRoomInfo(SuppData_PricingRoom SuppDataPricingRoom, SuppData_ShortInfo SuppDataInfo)
        {
            if (SuppDataInfo == null) return;
            
            FillImportantInformation(SuppDataPricingRoom, SuppDataInfo);
            FillArrivalAndDepartureTime(SuppDataPricingRoom, SuppDataInfo);
        }

        #region Private methods
        private void FillArrivalAndDepartureTime(SuppData_PricingRoom SuppDataPricingRoom, SuppData_ShortInfo SuppDataInfo)
        {
            if (SuppDataPricingRoom.ArrivalDateTime.IsEmptyTime())
            {
                if (SuppDataInfo.StandartCheckInTime.HasValue)
                    SuppDataPricingRoom.ArrivalDateTime = SuppDataPricingRoom.ArrivalDateTime.Date + SuppDataInfo.StandartCheckInTime.Value;
                else if (SuppDataPricingRoom.SupplierId == Constants.SuppDataSuppliers.Ostrovok)
                {
                    SuppDataInfo.StandartCheckInTime = ostrovokStandartCheckInTime;
                    SuppDataPricingRoom.ArrivalDateTime = SuppDataPricingRoom.ArrivalDateTime.Date + ostrovokStandartCheckInTime;
                }
            }

            if (SuppDataPricingRoom.DepartureDateTime.IsEmptyTime())
            {
                if (SuppDataInfo.StandartCheckOutTime.HasValue)
                    SuppDataPricingRoom.DepartureDateTime = SuppDataPricingRoom.DepartureDateTime.Date + SuppDataInfo.StandartCheckOutTime.Value;
                else if (SuppDataPricingRoom.SupplierId == Constants.SuppDataSuppliers.Ostrovok)
                {
                    SuppDataInfo.StandartCheckOutTime = ostrovokStandartCheckOutTime;
                    SuppDataPricingRoom.DepartureDateTime = SuppDataPricingRoom.DepartureDateTime.Date + ostrovokStandartCheckOutTime;
                }
            }
        }
        
        private void FillImportantInformation(SuppData_PricingRoom SuppDataPricingRoom, SuppData_ShortInfo SuppDataInfo)
        {
            var importantInformation = SuppDataInfo.ImportantInformation?.FirstOrDefault(i => i.SupplierId == SuppDataPricingRoom.SupplierId);

            if (importantInformation == null)
            {
                return;
            }

            SuppDataPricingRoom.AddiditionalParams.RoomImportantInformation = $"{SuppDataPricingRoom.AddiditionalParams.RoomImportantInformation} {Environment.NewLine}{importantInformation.Russian}";
            SuppDataPricingRoom.AddiditionalParams.RoomImportantInformationEn = $"{SuppDataPricingRoom.AddiditionalParams.RoomImportantInformationEn} {Environment.NewLine}{importantInformation.English}";
        }

        #endregion
    }
}