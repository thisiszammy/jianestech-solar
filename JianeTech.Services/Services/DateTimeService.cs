using JianeTech.Services.Interfaces;

namespace JianeTech.Services.Services
{
    public class DateTimeService : IDateTimeService
    {
        public DateTime Now() => DateTime.Now;

        public DateTime Today() => DateTime.Today;
    }
}
