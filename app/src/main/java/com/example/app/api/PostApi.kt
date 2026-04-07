package com.example.app.api

import com.google.gson.annotations.SerializedName

data class CreatePostRequest(
    @SerializedName("content") val content: String,
    @SerializedName("authorLogin") val authorLogin: String,
    @SerializedName("isImportant") val isImportant: Boolean,
    @SerializedName("poll") val poll: PollCreateRequest? = null
)

data class PollCreateRequest(
    @SerializedName("question") val question: String,
    @SerializedName("description") val description: String? = null,
    @SerializedName("options") val options: List<String>,
    @SerializedName("allowMediaInQuestionAndOptions") val allowMediaInQuestionAndOptions: Boolean = false,
    @SerializedName("showVoters") val showVoters: Boolean = false,
    @SerializedName("allowRevote") val allowRevote: Boolean = true,
    @SerializedName("shuffleOptions") val shuffleOptions: Boolean = false,
    @SerializedName("endsAtUtc") val endsAtUtc: String? = null,
    @SerializedName("hideResultsUntilEnd") val hideResultsUntilEnd: Boolean = false,
    @SerializedName("creatorCanViewWithoutVoting") val creatorCanViewWithoutVoting: Boolean = true
)

data class PollItem(
    @SerializedName("question") val question: String,
    @SerializedName("description") val description: String? = null,
    @SerializedName("options") val options: List<PollOptionItem>,
    @SerializedName("allowMediaInQuestionAndOptions") val allowMediaInQuestionAndOptions: Boolean = false,
    @SerializedName("showVoters") val showVoters: Boolean = false,
    @SerializedName("allowRevote") val allowRevote: Boolean = true,
    @SerializedName("shuffleOptions") val shuffleOptions: Boolean = false,
    @SerializedName("endsAtUtc") val endsAtUtc: String? = null,
    @SerializedName("hideResultsUntilEnd") val hideResultsUntilEnd: Boolean = false,
    @SerializedName("creatorCanViewWithoutVoting") val creatorCanViewWithoutVoting: Boolean = true,
    @SerializedName("totalVotes") val totalVotes: Int = 0,
    @SerializedName("hasVoted") val hasVoted: Boolean = false,
    @SerializedName("selectedOptionId") val selectedOptionId: Int? = null,
    @SerializedName("canViewResults") val canViewResults: Boolean = false
)

data class PollOptionItem(
    @SerializedName("id") val id: Int,
    @SerializedName("text") val text: String,
    @SerializedName("votesCount") val votesCount: Int = 0,
    @SerializedName("voters") val voters: List<PollVoterItem>? = null
)

data class PollVoterItem(
    @SerializedName("login") val login: String,
    @SerializedName("avatarUrl") val avatarUrl: String? = null
)

data class PostItem(
    @SerializedName("id") val id: Int,
    @SerializedName("authorLogin") val authorLogin: String,
    @SerializedName("authorName") val authorName: String,
    @SerializedName("content") val content: String,
    @SerializedName("createdAt") val createdAt: String,
    @SerializedName("imageUrl") val imageUrl: String?,
    @SerializedName("isImportant") val isImportant: Boolean,
    @SerializedName("expiresAt") val expiresAt: String?,
    @SerializedName("likesCount") val likesCount: Int,
    @SerializedName("commentsCount") val commentsCount: Int,
    @SerializedName("poll") val poll: PollItem? = null
)

data class CreatePostResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("post") val post: PostItem?
)

data class FeedResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("posts") val posts: List<PostItem>?
)

data class DeletePostResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)

data class VoteRequest(
    @SerializedName("login") val login: String,
    @SerializedName("optionId") val optionId: Int
)

data class VoteResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("poll") val poll: PollItem? = null
)
