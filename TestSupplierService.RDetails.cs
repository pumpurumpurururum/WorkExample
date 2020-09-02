using MyCompany.Core.Extensions;
using MyCompany.Core.Helpers;
using MyCompany.Core.Validation;
using MyCompany.TestSupplier.Extensions;
using MyCompany.Concrete.Api.Base.Errors;
using MyCompany.Concrete.Api.Objects.Hotel;
using MyCompany.Concrete.Api.Services.Hotel.Pricing;
using MyCompany.Concrete.Api.Services.Hotel.RoomDetail;
using MyCompany.Platform.ObjectModel.Concrete.Common;
using IO.Swagger.Model;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MyCompany.TestSupplier.Services
{
    public partial class TestSupplierService
    {
        public async Task<RoomDetailResponse> RoomDetailsAsync(RoomDetailRequest request)
        {
            return await RoomDetailsInternalAsync(request);
        }
        //
        public async Task<RoomDetailResponse> RoomDetailsInternalAsync(RoomDetailRequest request, bool skipSearchSimilarOffer = false)
        {
            var bookingCodeInfo = new RateInfo();
            bookingCodeInfo.FillFromString(request.BookingCode);
            Guard.ValidateExpression(() => bookingCodeInfo.ArrivalDate == DateTime.MinValue, "Дата заезда не указана");
            Guard.ValidateExpression(() => bookingCodeInfo.DepartureDate == DateTime.MinValue, "Дата выезда не указана");
            Guard.ValidateExpression(() => bookingCodeInfo.HotelID == 0, "Отель не указан");
            Guard.ValidateExpression(() => string.IsNullOrEmpty(bookingCodeInfo.HotelRateCode), "Не указан кодированный код бронирования");

            var searchId = string.Empty;

            var pureTotalPrice = bookingCodeInfo.PureTotalPrice?.Amount ?? 0;
            var numberOfGuests = 0;

            var supplierRateInfo = bookingCodeInfo.ConvertToRateCode();
            if (supplierRateInfo != null)
            {
                searchId = supplierRateInfo.SearchId;

                numberOfGuests = supplierRateInfo.NumberOfGuests;
            }

            RoomDetailResponse result = null;
            try
            {
                result = await ExecuteRoomDetailAsync(searchId, supplierRateInfo.OfferId, numberOfGuests, request.Language, request.ClientId, request.EmployeeId, bookingCodeInfo);
            }
            catch (Exception outerException)
            {
                _logger.Error(outerException);

                

                Guard.SupplierException(() => supplierRateInfo == null, "Удовлетворяющих критериям запроса номеров не найдено", SubType.RateNotAvaliable);
                try
                {
                    if (skipSearchSimilarOffer)
                        throw;

                    var pricing = await PricingInternalAsync(new PricingRequest
                    {
                        ArrivalDate = supplierRateInfo.StartDate,
                        DepartureDate = supplierRateInfo.EndDate,
                        HotelId = (int) supplierRateInfo.HotelId,
                        NumberOfGuests = supplierRateInfo.NumberOfGuests,
                        ClientId = request.ClientId,
                        EmployeeId = request.EmployeeId,
                        AddiditionalParams = new PricingAddiditionalParams
                        {
                            SkipRateInfoRequestFromSupplier = false
                        },
                        Language = bookingCodeInfo.Language
                    }, request.BookingCode,true);

                    var errorMessages = pricing?.Messages != null && pricing.Messages.Any()
                        ? string.Join(", ", pricing.Messages)
                        : string.Empty;

                    Guard.SupplierException(
                        () => pricing.HotelAvaibility.Rooms == null || !pricing.HotelAvaibility.Rooms.Any(),
                        errorMessages, SubType.RateNotAvaliable);

                    var room = pricing.HotelAvaibility.Rooms.FirstOrDefault(r =>
                        PricingRoomHelper.GetReplacingModel(r, bookingCodeInfo).IsCanBeReplaced);

                    Guard.SupplierException(() => room == null, "Удовлетворяющих критериям запроса номеров не найдено",
                        SubType.RateNotAvaliable);

                    bookingCodeInfo.FillFromString(room.BookingCode);

                    var rateCode = bookingCodeInfo.ConvertToRateCode();

                    var detailsSecondResponse = await HotelSearchDetailsAsync(rateCode.SearchId, rateCode.OfferId,
                        request.Language, request.ClientId, request.EmployeeId);

                    var hotelPricingResponse = _mappers.TryConvert(detailsSecondResponse, bookingCodeInfo, _supplierId,
                        room.NumberOfGuests, request.Language);

                    result = PricingRoomHelper.GetRoomDetailResponse(hotelPricingResponse.HotelAvaibility.Rooms,
                        bookingCodeInfo);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                    throw outerException;
                }
            }

            if (result != null)
            {
                _messageBusSender.SendHotelPriceCompareBeforeBookingMessage(request.ServiceId, _supplierId, pureTotalPrice, result.Room.TotalPrice.Amount);
            }

            Guard.SupplierException(() => result == null, "Удовлетворяющих критериям запроса номеров не найдено", SubType.RateNotAvaliable);
            return result;
        }


        private async Task<RoomDetailResponse> ExecuteRoomDetailAsync(string searchId, string offerId, int numberOfGuests, Language language, int clientId, int employeeId, RateInfo bookingCodeInfo)
        {
            var detailsResponse = await HotelSearchDetailsAsync(searchId, offerId, language, clientId, employeeId);

            Guard.SupplierException(() => detailsResponse == null, "Ошибка выполнения запроса HotelSearchDetails", SubType.RateNotAvaliable);

            //todo: rackbar
            var hotelPricingResponse = _mappers.TryConvert(detailsResponse, bookingCodeInfo, _supplierId, numberOfGuests, language);

            Guard.SupplierException(() => hotelPricingResponse.HotelAvaibility?.Rooms == null || !hotelPricingResponse.HotelAvaibility.Rooms.Any(), "Удовлетворяющих критериям запроса номеров не найдено", SubType.RateNotAvaliable);

            var room = hotelPricingResponse.HotelAvaibility?.Rooms?.FirstOrDefault(r => PricingRoomHelper.GetReplacingModel(r, bookingCodeInfo).IsCanBeReplaced);
            Guard.SupplierException(() => room == null, "Параметры найденной комнаты изменились", SubType.RateNotAvaliable);

            var response = new RoomDetailResponse
            {
                Room = room,
                Messages = hotelPricingResponse.Messages,
                Success = hotelPricingResponse.Success
            };

            return response;
        }

        internal async Task<SearchDetailsResponse> HotelSearchDetailsAsync(string searchId, string offerId, Language language, int clientId, int employeeId)
        {
            var supplierRequest = _mappers.TryConvert(searchId, offerId, employeeId, clientId, language);

            var response = await _apiWrapperService.PostSearchDetailsRequestCollectionWithHttpInfo(supplierRequest);
            return response;

        }

    }
}
