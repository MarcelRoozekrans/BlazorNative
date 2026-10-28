# A SCRIPT THAT FINISHES, for GeneratorRunTests' #551 facts: it prints one line and
# exits with the code it is given, so a run of it is quick and its result is checkable.
param([int]$Code = 0)

Write-Output 'exit-code stub ran'
exit $Code
