package com.example.app.api

import com.google.gson.annotations.SerializedName

data class EmployeeWorkSchedule(
    @SerializedName("workPattern") val workPattern: String,
    @SerializedName("shiftStart") val shiftStart: String,
    @SerializedName("shiftEnd") val shiftEnd: String,
    @SerializedName("vacationStart") val vacationStart: String,
    @SerializedName("vacationEnd") val vacationEnd: String
)

data class EmployeeWorkScheduleResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("schedule") val schedule: EmployeeWorkSchedule?
)
