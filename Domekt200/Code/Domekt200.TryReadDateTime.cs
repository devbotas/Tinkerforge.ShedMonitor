﻿using System;

namespace IotFleet.Shed;

partial class Domekt200 {
    private bool TryReadDateTime(out DateTime dateTime) {
        _reliableModbus.TryReadModbusRegister(KomfoventRegisters.HourAndMinute, out var hourAndMinute);
        var hour = hourAndMinute >> 8;
        var minute = hourAndMinute & 0xF;

        _reliableModbus.TryReadModbusRegister(KomfoventRegisters.MonthAndDay, out var monthAndDay);
        var month = monthAndDay >> 8;
        var day = monthAndDay & 0xFF;

        _reliableModbus.TryReadModbusRegister(KomfoventRegisters.Year, out var year);

        bool succeeded;
        try {
            dateTime = new DateTime(year, month, day, hour, minute, 0);
            succeeded = true;
        }
        catch {
            // Sometime conversion crashes because Domekt return some insane value. Probably due to some internal race conditions.
            dateTime = DateTime.Now;
            succeeded = false;
        }

        return succeeded;
    }
}
