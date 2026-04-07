package com.example.app.api

import com.google.gson.annotations.SerializedName

data class EmployeeProfile(
    @SerializedName("lastName") val lastName: String,
    @SerializedName("firstName") val firstName: String,
    @SerializedName("phone") val phone: String,
    @SerializedName("employeeId") val employeeId: String,
    @SerializedName("position") val position: String,
    @SerializedName("subdivision") val subdivision: String,
    @SerializedName("avatarUrl") val avatarUrl: String? = null,
    @SerializedName("level") val level: Int = 1,
    @SerializedName("experience") val experience: Int = 0,
    @SerializedName("xpToNext") val xpToNext: Int = 100
)

data class AvatarUploadResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("avatarUrl") val avatarUrl: String? = null
)

data class EmployeeProfileResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("profile") val profile: EmployeeProfile?
)

