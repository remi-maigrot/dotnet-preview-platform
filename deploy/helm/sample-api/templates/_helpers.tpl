{{/* Chart name, truncated to the 63 characters allowed in labels. */}}
{{- define "sample-api.name" -}}
{{- .Chart.Name | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/* Fully qualified name: the release name, suffixed with the chart name unless it already contains it. */}}
{{- define "sample-api.fullname" -}}
{{- if contains .Chart.Name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}

{{- define "sample-api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "sample-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/* Labels shared by every object of the release. */}}
{{- define "sample-api.commonLabels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
{{ include "sample-api.selectorLabels" . }}
app.kubernetes.io/version: {{ .Values.image.tag | quote }}
app.kubernetes.io/part-of: preview-platform
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "sample-api.labels" -}}
{{ include "sample-api.commonLabels" . }}
app.kubernetes.io/component: api
{{- end }}

{{- define "sample-api.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "sample-api.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/* image reference: repository:tag, or repository@digest when a digest is given. */}}
{{- define "sample-api.imageRef" -}}
{{- $tag := required "image.tag is required (use the git commit SHA)" .tag -}}
{{- if .digest }}
{{- printf "%s@%s" .repository .digest }}
{{- else }}
{{- printf "%s:%s" .repository $tag }}
{{- end }}
{{- end }}

{{- define "sample-api.image" -}}
{{- include "sample-api.imageRef" .Values.image }}
{{- end }}

{{- define "sample-api.migratorImage" -}}
{{- $image := dict "repository" .Values.migrations.image.repository "tag" (default .Values.image.tag .Values.migrations.image.tag) "digest" .Values.migrations.image.digest -}}
{{- include "sample-api.imageRef" $image }}
{{- end }}

{{- define "sample-api.backupImage" -}}
{{- $image := dict "repository" .Values.backup.image.repository "tag" (default .Values.image.tag .Values.backup.image.tag) "digest" .Values.backup.image.digest -}}
{{- include "sample-api.imageRef" $image }}
{{- end }}

{{/* Environment shared by the API and the migration job. */}}
{{- define "sample-api.commonEnv" -}}
- name: ASPNETCORE_ENVIRONMENT
  value: {{ .Values.environment | quote }}
- name: OTEL_SERVICE_NAME
  value: {{ include "sample-api.name" . | quote }}
{{- with .Values.opentelemetry.endpoint }}
- name: OTEL_EXPORTER_OTLP_ENDPOINT
  value: {{ . | quote }}
{{- end }}
# The SDK pushes metrics every 60 s by default; Prometheus needs several points per rate() window.
- name: OTEL_METRIC_EXPORT_INTERVAL
  value: {{ .Values.opentelemetry.metricExportIntervalMs | quote }}
- name: OTEL_RESOURCE_ATTRIBUTES
  value: {{ printf "k8s.namespace.name=%s,service.instance.id=$(POD_NAME)%s" .Release.Namespace (ternary (printf ",%s" .Values.opentelemetry.resourceAttributes) "" (ne .Values.opentelemetry.resourceAttributes "")) | quote }}
{{- end }}
