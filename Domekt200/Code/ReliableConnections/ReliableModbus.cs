﻿using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;


namespace IotFleet.Shed;

class ReliableModbus {
    private string _deviceIp = "localhost";
    private CancellationTokenSource _globalCancellationTokenSource;
    private readonly Logger _log = LogManager.GetCurrentClassLogger();
    private SharpModbus.ModbusMaster _modbus;
    private readonly object _modbusLock = new();
    private IExceptionlessSocket _socket;

    public bool IsConnected { get; private set; }
    public bool IsInitialized { get; private set; }
    public Exception LastException { get; private set; }
    public int DisconnectCount { get; private set; }

    public void Initialize(string modbusDeviceIpAddress) {
        if (IsInitialized) { return; }

        _globalCancellationTokenSource = new CancellationTokenSource();

        _deviceIp = modbusDeviceIpAddress;

        _modbus = new SharpModbus.ModbusMaster();

        Task.Run(async () => await MonitorConnectionContinuously(_globalCancellationTokenSource.Token));

        IsInitialized = true;
    }
    public bool TryReadModbusRegister(KomfoventRegisters register, out int value) {
        if (IsInitialized == false) {
            value = 0;
            return false;
        }


        var returnResult = false;
        value = 0;

        try {

            value = _modbus.ReadHoldingRegister(2, (ushort)((ushort)register - 1));
            Thread.Sleep(10);
            returnResult = true;
        }
        catch (Exception ex) {
            _log.Warn($"Could not read ModBus register {register}, because of {ex.Message}.");
            IsConnected = false;
        }

        return returnResult;
    }

    public bool TryWriteModbusRegister(KomfoventRegisters register, int value) {
        if (IsInitialized == false) { return false; }

        var returnResult = false;

        try {
            _modbus.WriteRegister(2, (ushort)((ushort)register - 1), (ushort)value);
        }
        catch (Exception ex) {
            _log.Warn($"Could not write ModBus register {register}, because of {ex.Message}.");
        }

        return returnResult;
    }

    private async Task MonitorConnectionContinuously(CancellationToken cancelationToken) {
        if (IsInitialized == false) { throw new InvalidOperationException("Call the initialize method first!"); }

        while (cancelationToken.IsCancellationRequested == false) {
            if (IsConnected == false) {
                DisconnectCount++;

                try {
                    _log.Info($"Connecting to Modbus device at {_deviceIp}.");
                    Connect();

                    _modbus.Initialize(WriteReadDevice);

                    IsConnected = true;
                }
                catch (Exception ex) {
                    _log.Error(ex, $"{nameof(MonitorConnectionContinuously)} tried to connect to broker, but that did not work.");
                }

                await Task.Delay(500, cancelationToken);
            }
            await Task.Delay(10, cancelationToken);
        }
    }

    private void Connect() {
        try {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.ReceiveTimeout = 5000;
            socket.SendTimeout = 1000;

            if (_deviceIp.ToLower() == "localhost") {
                // Localhost works fine on Windows, but we experienced problems on Linux machines.
                // Also, connecting to "localhost" will have to go to DNS which is much slower than simply using 127.0.0.1.
                // Therefore I replace it here and hopefully save some debugging hours.
                socket.Connect("127.0.0.1", 502);
            }
            else {
                socket.Connect(_deviceIp, 502);
            }

            _socket = new TcpSocketWrapper(socket);

            IsConnected = true;
        }
        catch (SocketException ex) {
            LastException = ex;
            DisconnectAndCleanup();
        }
    }
    private void DisconnectAndCleanup() {
        IsConnected = false;
        _socket?.Dispose();
        _socket = null;
    }
    private bool WriteReadDevice(byte[] sendBuffer, byte[] receiveBuffer) {
        var isOk = true;

        if (IsConnected == false) { isOk = false; }

        if (isOk) {
            isOk = _socket.TrySend(sendBuffer, 0, sendBuffer.Length);

            if (isOk == false) {
                _log.Warn("Couldn't send to ModBus device. Disconnecting.");
                DisconnectAndCleanup();
            }
        }

        if (isOk) { Thread.Sleep(100); }

        if (isOk) {
            var (IsOk, _) = _socket.TryReceive(receiveBuffer, 0, receiveBuffer.Length, receiveBuffer.Length);
            if (IsOk == false) {
                _log.Warn("Couldn't receive from ModBus device. Disconnecting.");
                isOk = false;
                DisconnectAndCleanup();
            }
        }

        if (isOk) { Thread.Sleep(50); }

        return isOk;
    }
}
