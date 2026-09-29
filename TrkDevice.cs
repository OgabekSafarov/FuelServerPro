namespace FuelServerPro
{
    public class TrkDevice
    {
        public byte Address { get; }
        public bool IsConnected { get; set; }
        public byte CurrentStateByte { get; set; }
        public long CurrentVolume { get; set; }
        public long CurrentAmount { get; set; }
        public long CurrentPrice { get; set; }
        public long TotalVolume { get; set; }
        public long TotalAmount { get; set; }
        public long ShiftVolume { get; set; }
        public long ShiftAmount { get; set; }
        public byte? ErrorCode { get; set; }
        public byte? SolenoidState { get; set; }
        public byte[]? SolenoidData { get; set; }
        public byte[]? DeviceId { get; set; }
        public byte[]? CardId { get; set; }

        public bool IsNozzleOff => (CurrentStateByte & 0x80) == 0;
        public bool IsNozzleHanged => (CurrentStateByte & 0x80) != 0;
        public bool IsPaused => (CurrentStateByte & 0x40) != 0;
        public bool IsFilling => (CurrentStateByte & 0x20) != 0;
        public bool IsPcControl => (CurrentStateByte & 0x08) != 0;
        public bool IsRemoteControl => IsPcControl;
        public bool IsPresetReady => (CurrentStateByte & 0x02) != 0;
        public bool HasError => (CurrentStateByte & 0x01) != 0;

        public TrkDevice(byte address)
        {
            Address = address;
        }
    }
}
