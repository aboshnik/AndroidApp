package com.example.app.api

import com.google.gson.annotations.SerializedName

data class EmployeeProfile(
    @SerializedName("lastName") val lastName: String,
    @SerializedName("firstName") val firstName: String,
    @SerializedName("phone") val phone: String,
    @SerializedName("employeeId") val employeeId: String,
    @SerializedName("position") val position: String,
    @SerializedName("subdivision") val subdivision: String
)

data class EmployeeProfileResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("profile") val profile: EmployeeProfile?
)

