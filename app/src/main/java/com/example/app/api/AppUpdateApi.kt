package com.example.app.api

import com.google.gson.annotations.SerializedName

data class AppLatestResponse(
    @SerializedName("versionCode") val versionCode: Int,
    @SerializedName("minSupportedVersion") val minSupportedVersion: Int? = null,
    @SerializedName("apkUrl") val apkUrl: String?,
    @SerializedName("message") val message: String? = null
)

